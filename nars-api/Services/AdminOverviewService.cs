using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class AdminOverviewService(AppDbContext db, IFeatureStatsService featureStatsService) : IAdminOverviewService
{
    public async Task<List<WilayaSummary>> GetNationalOverviewAsync(CancellationToken cancellationToken = default)
    {
        var wilayas = await db.Wilayas.OrderBy(w => w.WilayaId).ToListAsync(cancellationToken);
        var wilayaIds = wilayas.Select(w => w.WilayaId).ToArray();

        var admins = (await db.Users
                .Where(u => u.Role == UserRoles.WilayaAdmin && u.WilayaId.HasValue && wilayaIds.Contains(u.WilayaId.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(u => u.WilayaId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var dairasByWilaya = (await db.Dairas
                .Where(d => wilayaIds.Contains(d.WilayaId))
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.WilayaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allDairaIds = dairasByWilaya.Values.SelectMany(d => d).Select(d => d.DairaId).ToArray();

        var communesByDaira = (await db.Communes
                .Where(c => allDairaIds.Contains(c.DairaId))
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.DairaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allCommuneIds = communesByDaira.Values.SelectMany(c => c).Select(c => c.CommuneId).ToArray();

        var communeUserCounts = await db.Users
            .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue && allCommuneIds.Contains(u.CommuneId.Value))
            .GroupBy(u => u.CommuneId!.Value)
            .Select(g => new { CommuneId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var userCountByCommune = communeUserCounts.ToDictionary(x => x.CommuneId, x => x.Count);

        return wilayas.Select(wilaya =>
        {
            var admin = admins.GetValueOrDefault(wilaya.WilayaId);
            var dairas = dairasByWilaya.GetValueOrDefault(wilaya.WilayaId) ?? [];
            var communeIds = dairas.SelectMany(d =>
                communesByDaira.GetValueOrDefault(d.DairaId) ?? []
            ).Select(c => c.CommuneId).ToArray();

            return new WilayaSummary(
                WilayaId: wilaya.WilayaId,
                WilayaNameFr: wilaya.WilayaFr ?? "",
                WilayaNameAr: wilaya.WilayaAr ?? "",
                WilayaAdmin: admin is null ? null : new AdminInfo(
                    admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
                DairaCount: dairas.Count,
                CommuneCount: communeIds.Length,
                CommuneUserCount: communeIds.Sum(cid => userCountByCommune.GetValueOrDefault(cid))
            );
        }).ToList();
    }

    public async Task<WilayaReport?> GetWilayaReportAsync(int wilayaId, CancellationToken cancellationToken = default)
    {
        var wilaya = await db.Wilayas.FindAsync([wilayaId], cancellationToken);
        if (wilaya is null) return null;

        var admin = await db.Users.FirstOrDefaultAsync(u =>
            u.Role == UserRoles.WilayaAdmin && u.WilayaId == wilayaId, cancellationToken);

        var dairas = await db.Dairas.Where(d => d.WilayaId == wilayaId)
            .OrderBy(d => d.DairaFr).ToListAsync(cancellationToken);

        var dairaIds = dairas.Select(d => d.DairaId).ToArray();

        var dairaAdmins = (await db.Users
                .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId.HasValue && dairaIds.Contains(u.DairaId.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(u => u.DairaId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var communeReports = await BuildCommunesForDairasAsync(dairaIds, cancellationToken);

        var dairaReports = dairas.Select(daira =>
        {
            var da = dairaAdmins.GetValueOrDefault(daira.DairaId);

            return new DairaReport(
                DairaId: daira.DairaId,
                DairaNameFr: daira.DairaFr ?? "",
                DairaNameAr: daira.DairaAr ?? "",
                DairaAdmin: da is null ? null : new AdminInfo(
                    da.Id.ToString(), da.Username, da.Name, da.Email, da.Role),
                Communes: communeReports.GetValueOrDefault(daira.DairaId) ?? []
            );
        }).ToList();

        return new WilayaReport(
            WilayaId: wilayaId,
            WilayaNameFr: wilaya.WilayaFr ?? "",
            WilayaNameAr: wilaya.WilayaAr ?? "",
            WilayaAdmin: admin is null ? null : new AdminInfo(
                admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
            Dairas: dairaReports
        );
    }

    public async Task<DairaReport?> GetDairaReportAsync(int dairaId, CancellationToken cancellationToken = default)
    {
        var daira = await db.Dairas.FindAsync([dairaId], cancellationToken);
        if (daira is null) return null;

        var admin = await db.Users.FirstOrDefaultAsync(u =>
            u.Role == UserRoles.DairaAdmin && u.DairaId == dairaId, cancellationToken);

        var communesByDaira = await BuildCommunesForDairasAsync([dairaId], cancellationToken);
        var communes = communesByDaira.GetValueOrDefault(dairaId) ?? [];

        return new DairaReport(
            DairaId: dairaId,
            DairaNameFr: daira.DairaFr,
            DairaNameAr: daira.DairaAr,
            DairaAdmin: admin is null ? null : new AdminInfo(
                admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
            Communes: communes
        );
    }

    private async Task<Dictionary<int, List<CommuneReport>>> BuildCommunesForDairasAsync(int[] dairaIds, CancellationToken cancellationToken = default)
    {
        var communes = await db.Communes.Where(c => dairaIds.Contains(c.DairaId))
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
            : new Dictionary<Guid, UserFeatureStats>();

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
}
