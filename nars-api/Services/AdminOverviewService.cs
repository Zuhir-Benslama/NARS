using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using Npgsql;

namespace NarsApi.Services;

public sealed class AdminOverviewService(AppDbContext db, IFeatureStatsService featureStatsService) : IAdminOverviewService
{
    public async Task<(List<WilayaSummary> Items, int Total)> GetNationalOverviewAsync(
        int skip = 0, int take = 500, CancellationToken cancellationToken = default)
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
                AdminReportQueries.NationalOverviewSql,
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
                AdminReportQueries.WilayaReportSql,
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
                ? new AdminInfo(
                    first.DairaAdminId.Value.ToString(),
                    first.DairaAdminUsername!,
                    first.DairaAdminName!,
                    first.DairaAdminEmail!,
                    first.DairaAdminRole!)
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
                AdminReportQueries.CommuneUsersSql,
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
}
