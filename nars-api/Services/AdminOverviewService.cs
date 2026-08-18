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
            w.wilaya_id, w.wilaya_fr, w.wilaya_ar,
            a.id AS admin_id, a.username AS admin_username,
            a.name AS admin_name, a.email AS admin_email, a.role AS admin_role,
            COALESCE(s.daira_count, 0) AS daira_count,
            COALESCE(s.commune_count, 0) AS commune_count,
            COALESCE(s.commune_user_count, 0) AS commune_user_count
        FROM wilayas w
        LEFT JOIN admin_cte a ON a.wilaya_id = w.wilaya_id
        LEFT JOIN stats_cte s ON s.wilaya_id = w.wilaya_id
        WHERE w.wilaya_id = ANY(@wilayaIds)
        ORDER BY w.wilaya_id
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

        // Project to a slim AdminInfo and order by CreatedAt so the chosen
        // admin is deterministic when duplicates exist (mirrors the national view).
        var admin = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRoles.WilayaAdmin && u.WilayaId == wilayaId)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new AdminInfo(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role))
            .FirstOrDefaultAsync(cancellationToken);

        var dairas = await db.Dairas.AsNoTracking().Where(d => d.WilayaId == wilayaId)
            .OrderBy(d => d.DairaFr).ToListAsync(cancellationToken);

        var dairaIds = dairas.Select(d => d.DairaId).ToArray();

        // Grouping tolerates duplicate admins per daira (non-unique filtered index).
        var dairaAdminList = await db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId.HasValue && dairaIds.Contains(u.DairaId.Value))
            .Select(u => new AdminPick(null, u.DairaId, u.Id, u.Username, u.Name, u.Email, u.Role, u.CreatedAt))
            .ToListAsync(cancellationToken);
        var dairaAdmins = dairaAdminList.GroupBy(u => u.DairaId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(u => u.CreatedAt).First());

        var communeReports = await BuildCommunesForDairasAsync(dairaIds, cancellationToken);

        var dairaReports = dairas.Select(daira =>
        {
            var da = dairaAdmins.GetValueOrDefault(daira.DairaId);

            return new DairaReport(
                DairaId: daira.DairaId,
                DairaNameFr: daira.DairaFr ?? "",
                DairaNameAr: daira.DairaAr ?? "",
                DairaAdmin: da is null ? null : da.ToAdminInfo(),
                Communes: communeReports.GetValueOrDefault(daira.DairaId) ?? []
            );
        }).ToList();

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

        var communesByDaira = await BuildCommunesForDairasAsync([dairaId], cancellationToken);
        var communes = communesByDaira.GetValueOrDefault(dairaId) ?? [];

        return new DairaReport(
            DairaId: dairaId,
            DairaNameFr: daira.DairaFr,
            DairaNameAr: daira.DairaAr,
            DairaAdmin: admin,
            Communes: communes
        );
    }

    private async Task<Dictionary<int, List<CommuneReport>>> BuildCommunesForDairasAsync(int[] dairaIds, CancellationToken cancellationToken = default)
    {
        var communes = await db.Communes.AsNoTracking().Where(c => dairaIds.Contains(c.DairaId))
            .OrderBy(c => c.CommuneFr).ToListAsync(cancellationToken);

        var communeIds = communes.Select(c => c.CommuneId).ToArray();

        var users = await db.Users
            .Where(u => u.CommuneId.HasValue && communeIds.Contains(u.CommuneId.Value))
            .Where(u => u.Role == UserRoles.CommuneUser)
            .Select(u => new { u.Id, u.Username, u.Name, u.Email, u.Role, u.CommuneId })
            .ToListAsync(cancellationToken);

        var userGroups = users.GroupBy(u => u.CommuneId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allUserIds = users.Select(u => u.Id).ToArray();
        var featureCounts = allUserIds.Length > 0
            ? await featureStatsService.GetUserFeatureCountsAsync(allUserIds, cancellationToken)
            : [];

        var result = new Dictionary<int, List<CommuneReport>>();
        foreach (var communeGroup in communes.GroupBy(c => c.DairaId))
        {
            var reports = communeGroup.Select(commune =>
            {
                var communeUsers = userGroups.GetValueOrDefault(commune.CommuneId);
                var userStats = (communeUsers ?? []).Select(u =>
                {
                    featureCounts.TryGetValue(u.Id, out var stats);
                    return stats ?? new UserFeatureStats(
                        u.Id.ToString(), u.Username, u.Name, u.Email, u.Role,
                        0, 0, 0, 0, 0, 0, 0, 0, 0
                    );
                }).ToList();

                return new CommuneReport(
                    CommuneId: commune.CommuneId,
                    CommuneNameFr: commune.CommuneFr ?? "",
                    CommuneNameAr: commune.CommuneAr ?? "",
                    Users: userStats
                );
            }).ToList();

            result[communeGroup.Key] = reports;
        }

        return result;
    }

    /// <summary>
    /// Slim admin projection used to build the overview reports. Loads only the
    /// display fields (no PasswordHash) and keeps CreatedAt for deterministic
    /// earliest-admin selection when duplicates exist.
    /// </summary>
    private sealed record AdminPick(
        int? WilayaId,
        int? DairaId,
        Guid Id,
        string Username,
        string Name,
        string Email,
        string Role,
        DateTime CreatedAt)
    {
        public AdminInfo ToAdminInfo() => new(Id.ToString(), Username, Name, Email, Role);
    }

    /// <summary>
    /// EF Core entity type for the CTE query result. Maps flat SQL columns
    /// to the WilayaSummary DTO after materialization.
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
}
