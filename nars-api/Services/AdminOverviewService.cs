using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using Npgsql;

namespace NarsApi.Services;

public sealed class AdminOverviewService(AppDbContext db, IFeatureStatsService featureStatsService) : IAdminOverviewService
{
    /// <summary>
    /// Combines admin lookup, daira/commune counts, and commune user counts
    /// into a single CTE query. Replaces 4 sequential DB round-trips with 1.
    /// </summary>
    private const string NationalOverviewSql = """
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
    private const string WilayaReportSql = """
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
    private const string CommuneUsersSql = """
        SELECT
            c.commune_id AS "CommuneId", c.commune_fr AS "CommuneFr", c.commune_ar AS "CommuneAr",
            cu.id AS "UserId", cu.username AS "UserUsername", cu.name AS "UserName",
            cu.email AS "UserEmail", cu.role AS "UserRole"
        FROM communes c
        LEFT JOIN users cu ON cu.commune_id = c.commune_id AND cu.role = 'commune_user'
        WHERE c.daira_id = @did
        ORDER BY c.commune_fr, cu.name
        """;

    public async Task<(List<WilayaSummary> Items, int Total)> GetNationalOverviewAsync(int skip = 0, int take = 500, CancellationToken cancellationToken = default)
    {
        var total = await db.Wilayas.CountAsync(cancellationToken);

        var pagedWilayas = await db.Wilayas
            .AsNoTracking()
            .OrderBy(w => w.WilayaId)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);
        var wilayaIds = pagedWilayas.Select(w => w.WilayaId).ToArray();

        // Single CTE query replaces 4 sequential round-trips (admins, dairas,
        // communes, commune user counts). DbContext is not thread-safe so
        // sequential queries were unavoidable; the CTE eliminates the need.
#pragma warning disable S2077 // Table and column names are hardcoded constants
        var rows = await db.Database.SqlQueryRaw<WilayaOverviewRow>(
                NationalOverviewSql,
                new NpgsqlParameter("@wilayaIds", wilayaIds))
            .ToListAsync(cancellationToken);
#pragma warning restore S2077

        var rowMap = rows.ToDictionary(r => r.WilayaId);

        var items = pagedWilayas.Select(wilaya =>
        {
            var row = rowMap.GetValueOrDefault(wilaya.WilayaId);

            AdminInfo? admin = row?.AdminId is not null
                ? new AdminInfo(row.AdminId.Value.ToString(), row.AdminUsername!, row.AdminName!, row.AdminEmail!, row.AdminRole!)
                : null;

            return new WilayaSummary(
                WilayaId: wilaya.WilayaId,
                WilayaNameFr: wilaya.WilayaFr ?? "",
                WilayaNameAr: wilaya.WilayaAr ?? "",
                WilayaAdmin: admin,
                DairaCount: row?.DairaCount ?? 0,
                CommuneCount: row?.CommuneCount ?? 0,
                CommuneUserCount: row?.CommuneUserCount ?? 0
            );
        }).ToList();

        return (items, total);
    }

    public async Task<WilayaReport?> GetWilayaReportAsync(int wilayaId, CancellationToken cancellationToken = default)
    {
        var wilaya = await db.Wilayas.AsNoTracking().FirstOrDefaultAsync(w => w.WilayaId == wilayaId, cancellationToken);
        if (wilaya is null)
        {
            return null;
        }

        // Single CTE replaces 4 sequential round-trips (dairas, daira admins,
        // communes, commune users). Feature counts remain a separate query.
#pragma warning disable S2077 // Table and column names are hardcoded constants
        var rows = await db.Database.SqlQueryRaw<WilayaReportRow>(
                WilayaReportSql,
                new NpgsqlParameter("@wid", wilayaId))
            .ToListAsync(cancellationToken);
#pragma warning restore S2077

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToArray();
        var featureCounts = userIds.Length > 0
            ? await featureStatsService.GetUserFeatureCountsAsync(userIds, cancellationToken)
            : [];

        // Project to a slim AdminInfo and order by CreatedAt so the chosen
        // admin is deterministic when duplicates exist (mirrors the national view).
        var admin = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRoles.WilayaAdmin && u.WilayaId == wilayaId)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new AdminInfo(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role))
            .FirstOrDefaultAsync(cancellationToken);

        var dairaGroups = rows.GroupBy(r => r.DairaId).ToList();
        var dairaReports = new List<DairaReport>(dairaGroups.Count);

        foreach (var group in dairaGroups)
        {
            var first = group.First();
            var dairaAdmin = first.DairaAdminId is not null
                ? new AdminInfo(first.DairaAdminId.Value.ToString(), first.DairaAdminUsername!, first.DairaAdminName!, first.DairaAdminEmail!, first.DairaAdminRole!)
                : null;

            var communeGroups = group
                .Where(r => r.CommuneId.HasValue)
                .GroupBy(r => r.CommuneId!.Value)
                .ToList();

            var communeReports = new List<CommuneReport>(communeGroups.Count);
            foreach (var cg in communeGroups)
            {
                var cFirst = cg.First();
                var users = cg.Where(r => r.UserId.HasValue).Select(r =>
                {
                    var uid = r.UserId!.Value;
                    featureCounts.TryGetValue(uid, out var stats);
                    return stats ?? new UserFeatureStats(
                        uid.ToString(), r.UserUsername!, r.UserName!, r.UserEmail!, r.UserRole!,
                        0, 0, 0, 0, 0, 0, 0, 0, 0);
                }).ToList();

                communeReports.Add(new CommuneReport(cFirst.CommuneId!.Value, cFirst.CommuneFr ?? "", cFirst.CommuneAr ?? "", users));
            }

            dairaReports.Add(new DairaReport(first.DairaId, first.DairaFr ?? "", first.DairaAr ?? "", dairaAdmin, communeReports));
        }

        return new WilayaReport(
            WilayaId: wilayaId,
            WilayaNameFr: wilaya.WilayaFr ?? "",
            WilayaNameAr: wilaya.WilayaAr ?? "",
            WilayaAdmin: admin,
            Dairas: dairaReports
        );
    }

    public async Task<DairaReport?> GetDairaReportAsync(int dairaId, int? expectedWilayaId = null, CancellationToken cancellationToken = default)
    {
        var daira = expectedWilayaId.HasValue
            ? await db.Dairas.AsNoTracking().FirstOrDefaultAsync(d => d.DairaId == dairaId && d.WilayaId == expectedWilayaId.Value, cancellationToken)
            : await db.Dairas.AsNoTracking().FirstOrDefaultAsync(d => d.DairaId == dairaId, cancellationToken);
        if (daira is null)
        {
            return null;
        }

        var admin = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId == dairaId)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new AdminInfo(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role))
            .FirstOrDefaultAsync(cancellationToken);

        // Single query replaces 2 sequential round-trips (communes, commune users).
#pragma warning disable S2077 // Table and column names are hardcoded constants
        var rows = await db.Database.SqlQueryRaw<CommuneUserRow>(
                CommuneUsersSql,
                new NpgsqlParameter("@did", dairaId))
            .ToListAsync(cancellationToken);
#pragma warning restore S2077

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToArray();
        var featureCounts = userIds.Length > 0
            ? await featureStatsService.GetUserFeatureCountsAsync(userIds, cancellationToken)
            : [];

        var communeGroups = rows.GroupBy(r => r.CommuneId).ToList();
        var communeReports = new List<CommuneReport>(communeGroups.Count);

        foreach (var cg in communeGroups)
        {
            var cFirst = cg.First();
            var users = cg.Where(r => r.UserId.HasValue).Select(r =>
            {
                var uid = r.UserId!.Value;
                featureCounts.TryGetValue(uid, out var stats);
                return stats ?? new UserFeatureStats(
                    uid.ToString(), r.UserUsername!, r.UserName!, r.UserEmail!, r.UserRole!,
                    0, 0, 0, 0, 0, 0, 0, 0, 0);
            }).ToList();

            communeReports.Add(new CommuneReport(cFirst.CommuneId, cFirst.CommuneFr ?? "", cFirst.CommuneAr ?? "", users));
        }

        return new DairaReport(
            DairaId: dairaId,
            DairaNameFr: daira.DairaFr,
            DairaNameAr: daira.DairaAr,
            DairaAdmin: admin,
            Communes: communeReports
        );
    }

    /// <summary>
    /// EF Core entity type for the national overview CTE query result.
    /// </summary>
    private sealed record WilayaOverviewRow(
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
    private sealed record WilayaReportRow(
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
    private sealed record CommuneUserRow(
        int CommuneId,
        string CommuneFr,
        string CommuneAr,
        Guid? UserId,
        string? UserUsername,
        string? UserName,
        string? UserEmail,
        string? UserRole);
}
