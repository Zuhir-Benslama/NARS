namespace NarsApi.Services;

/// <summary>
/// Raw SQL and EF Core row types backing <see cref="AdminOverviewService"/>.
/// Kept here so the service class stays focused on projection logic.
/// </summary>
internal static class AdminReportQueries
{
    /// <summary>
    /// Combines admin lookup, daira/commune counts, and commune user counts
    /// into a single CTE query. Replaces 4 sequential DB round-trips with 1.
    /// </summary>
    public const string NationalOverviewSql = """
        WITH admin_cte AS (
            SELECT DISTINCT ON (u.wilaya_id)
                u.wilaya_id, u.id, u.username, u.name, u.email, u.role
            FROM users u
            WHERE u.role = 'wilaya_admin' AND u.wilaya_id = ANY(@wilayaIds)
            ORDER BY u.wilaya_id, u.created_at
        ),
        stats_cte AS (
            SELECT
                d.wilaya_id,
                COUNT(DISTINCT d.daira_id) AS daira_count,
                COUNT(DISTINCT c.commune_id) AS commune_count,
                COUNT(u.id) AS commune_user_count
            FROM dairas d
            LEFT JOIN communes c ON c.daira_id = d.daira_id
            LEFT JOIN users u ON u.commune_id = c.commune_id AND u.role = 'commune_user'
            WHERE d.wilaya_id = ANY(@wilayaIds)
            GROUP BY d.wilaya_id
        )
        SELECT
            w.wilaya_id AS "WilayaId", w.wilaya_fr AS "WilayaFr", w.wilaya_ar AS "WilayaAr",
            a.id AS "AdminId", a.username AS "AdminUsername",
            a.name AS "AdminName", a.email AS "AdminEmail", a.role AS "AdminRole",
            COALESCE(s.daira_count, 0) AS "DairaCount",
            COALESCE(s.commune_count, 0) AS "CommuneCount",
            COALESCE(s.commune_user_count, 0) AS "CommuneUserCount"
        FROM wilayas w
        LEFT JOIN admin_cte a ON a.wilaya_id = w.wilaya_id
        LEFT JOIN stats_cte s ON s.wilaya_id = w.wilaya_id
        WHERE w.wilaya_id = ANY(@wilayaIds)
        ORDER BY w.wilaya_id
        """;

    /// <summary>
    /// Flat CTE returning daira + daira admin + commune + commune user rows
    /// for a single wilaya. Replaces 4 sequential round-trips (dairas, daira
    /// admins, communes, commune users) with 1.
    /// </summary>
    public const string WilayaReportSql = """
        WITH daira_admins AS (
            SELECT DISTINCT ON (u.daira_id)
                u.daira_id, u.id AS admin_id, u.username AS admin_username,
                u.name AS admin_name, u.email AS admin_email, u.role AS admin_role
            FROM users u
            WHERE u.role = 'daira_admin' AND u.daira_id IS NOT NULL
              AND u.daira_id IN (SELECT d.daira_id FROM dairas d WHERE d.wilaya_id = @wid)
            ORDER BY u.daira_id, u.created_at
        )
        SELECT
            d.daira_id AS "DairaId", d.daira_fr AS "DairaFr", d.daira_ar AS "DairaAr",
            da.admin_id AS "DairaAdminId", da.admin_username AS "DairaAdminUsername",
            da.admin_name AS "DairaAdminName", da.admin_email AS "DairaAdminEmail",
            da.admin_role AS "DairaAdminRole",
            c.commune_id AS "CommuneId", c.commune_fr AS "CommuneFr", c.commune_ar AS "CommuneAr",
            cu.id AS "UserId", cu.username AS "UserUsername", cu.name AS "UserName",
            cu.email AS "UserEmail", cu.role AS "UserRole"
        FROM dairas d
        LEFT JOIN daira_admins da ON da.daira_id = d.daira_id
        LEFT JOIN communes c ON c.daira_id = d.daira_id
        LEFT JOIN users cu ON cu.commune_id = c.commune_id AND cu.role = 'commune_user'
        WHERE d.wilaya_id = @wid
        ORDER BY d.daira_fr, c.commune_fr, cu.name
        """;

    /// <summary>
    /// Flat query returning commune + commune user rows for a single daira.
    /// Replaces 2 sequential round-trips (communes, commune users) with 1.
    /// </summary>
    public const string CommuneUsersSql = """
        SELECT
            c.commune_id AS "CommuneId", c.commune_fr AS "CommuneFr", c.commune_ar AS "CommuneAr",
            cu.id AS "UserId", cu.username AS "UserUsername", cu.name AS "UserName",
            cu.email AS "UserEmail", cu.role AS "UserRole"
        FROM communes c
        LEFT JOIN users cu ON cu.commune_id = c.commune_id AND cu.role = 'commune_user'
        WHERE c.daira_id = @did
        ORDER BY c.commune_fr, cu.name
        """;
}

/// <summary>EF Core entity type for the national overview CTE query result.</summary>
internal sealed record WilayaOverviewRow(
    int WilayaId,
    string WilayaFr,
    string WilayaAr,
    Guid? AdminId,
    string? AdminUsername,
    string? AdminName,
    string? AdminEmail,
    string? AdminRole,
    int DairaCount,
    int CommuneCount,
    int CommuneUserCount);

/// <summary>
/// EF Core entity type for the wilaya report CTE. Flat rows containing
/// daira + daira admin + commune + commune user data.
/// </summary>
internal sealed record WilayaReportRow(
    int DairaId,
    string DairaFr,
    string DairaAr,
    Guid? DairaAdminId,
    string? DairaAdminUsername,
    string? DairaAdminName,
    string? DairaAdminEmail,
    string? DairaAdminRole,
    int? CommuneId,
    string? CommuneFr,
    string? CommuneAr,
    Guid? UserId,
    string? UserUsername,
    string? UserName,
    string? UserEmail,
    string? UserRole);

/// <summary>
/// EF Core entity type for the commune user query. Flat rows containing
/// commune + commune user data for a single daira.
/// </summary>
internal sealed record CommuneUserRow(
    int CommuneId,
    string CommuneFr,
    string CommuneAr,
    Guid? UserId,
    string? UserUsername,
    string? UserName,
    string? UserEmail,
    string? UserRole);
