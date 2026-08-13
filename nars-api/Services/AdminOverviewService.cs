using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class AdminOverviewService(AppDbContext db, IFeatureStatsService featureStatsService) : IAdminOverviewService
{
    public async Task<(List<WilayaSummary> Items, int Total)> GetNationalOverviewAsync(int skip = 0, int take = 500, CancellationToken cancellationToken = default)
    {
        var total = await db.Wilayas.CountAsync(cancellationToken);

        var pagedWilayas = await db.Wilayas
            .AsNoTracking()
            .OrderBy(w => w.WilayaId)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);
        var wilayaIds = pagedWilayas.Select(w => w.WilayaId).ToArray();

        // Run sequentially — DbContext is not thread-safe. Project to a slim
        // row (no PasswordHash) and group by wilaya: the filtered index is
        // non-unique, so the earliest-created admin wins instead of crashing
        // on a duplicate key.
        var adminList = await db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRoles.WilayaAdmin && u.WilayaId.HasValue && wilayaIds.Contains(u.WilayaId.Value))
            .Select(u => new AdminPick(u.WilayaId, null, u.Id, u.Username, u.Name, u.Email, u.Role, u.CreatedAt))
            .ToListAsync(cancellationToken);
        var admins = adminList.GroupBy(u => u.WilayaId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(u => u.CreatedAt).First());

        var dairas = await db.Dairas
            .AsNoTracking()
            .Where(d => wilayaIds.Contains(d.WilayaId))
            .ToListAsync(cancellationToken);

        var dairaCounts = dairas
            .GroupBy(d => d.WilayaId)
            .ToDictionary(g => g.Key, g => g.Count());

        var allDairaIds = dairas.Select(d => d.DairaId).ToArray();

        var communes = await db.Communes
            .AsNoTracking()
            .Where(c => allDairaIds.Contains(c.DairaId))
            .ToListAsync(cancellationToken);
        var communesByDaira = communes
            .GroupBy(c => c.DairaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allCommuneIds = communesByDaira.Values.SelectMany(c => c).Select(c => c.CommuneId).ToArray();

        var userCountByCommune = await db.Users
            .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue && allCommuneIds.Contains(u.CommuneId.Value))
            .GroupBy(u => u.CommuneId!.Value)
            .Select(g => new { CommuneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CommuneId, x => x.Count, cancellationToken);

        var items = pagedWilayas.Select(wilaya =>
        {
            var admin = admins.GetValueOrDefault(wilaya.WilayaId);
            var wilayaDairas = dairas.Where(d => d.WilayaId == wilaya.WilayaId).ToList();
            var communeIds = wilayaDairas.SelectMany(d =>
                communesByDaira.GetValueOrDefault(d.DairaId) ?? []
            ).Select(c => c.CommuneId).ToArray();

            return new WilayaSummary(
                WilayaId: wilaya.WilayaId,
                WilayaNameFr: wilaya.WilayaFr ?? "",
                WilayaNameAr: wilaya.WilayaAr ?? "",
                WilayaAdmin: admin is null ? null : admin.ToAdminInfo(),
                DairaCount: dairaCounts.GetValueOrDefault(wilaya.WilayaId),
                CommuneCount: communeIds.Length,
                CommuneUserCount: communeIds.Sum(cid => userCountByCommune.GetValueOrDefault(cid))
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
}
