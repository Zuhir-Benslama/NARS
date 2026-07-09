using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class FeatureStatsService(IDbContextFactory<AppDbContext> dbFactory) : IFeatureStatsService
{
    public async Task<Dictionary<string, long>> GetFeatureCountsAsync(Guid userId, CancellationToken ct = default)
    {
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        var tasks = descriptors.Select(async d =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var count = await d.GetDbSet(db)
                .Where(f => f.UserId == userId)
                .LongCountAsync(ct);
            return (d.Type, Count: count);
        });
        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.Type, r => r.Count);
    }

    public async Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds, CancellationToken ct = default)
    {
        if (userIds.Length == 0)
        {
            return [];
        }

        await using var userDb = await dbFactory.CreateDbContextAsync(ct);
        var users = await userDb.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(ct);

        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        var featureCounts = users.ToDictionary(u => u.Id, _ => descriptors.ToDictionary(d => d.Type, _ => 0L));

        var tasks = descriptors.Select(async d =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var perUser = await d.GetDbSet(db)
                .Where(f => userIds.Contains(f.UserId))
                .GroupBy(f => f.UserId)
                .Select(g => new { UserId = g.Key, Count = g.LongCount() })
                .ToListAsync(ct);
            return (Type: d.Type, Data: perUser);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            foreach (var item in result.Data)
            {
                if (featureCounts.TryGetValue(item.UserId, out var counts))
                {
                    counts[result.Type] = item.Count;
                }
            }
        }

        var resultList = new Dictionary<Guid, UserFeatureStats>(users.Count);
        foreach (var user in users)
        {
            var counts = featureCounts[user.Id];
            long GetCount(string type) => counts.GetValueOrDefault(type, 0);
            var total = counts.Values.Sum();

            resultList[user.Id] = new UserFeatureStats(
                UserId: user.Id.ToString(),
                Username: user.Username,
                Name: user.Name,
                Email: user.Email,
                Role: user.Role,
                Areas: GetCount(FeatureTypes.Area),
                Districts: GetCount(FeatureTypes.District),
                CityCenters: GetCount(FeatureTypes.CityCenter),
                Roads: GetCount(FeatureTypes.Road),
                HouseEntrances: GetCount(FeatureTypes.HouseEntrance),
                PublicBuildings: GetCount(FeatureTypes.PublicBuilding),
                PublicSpaces: GetCount(FeatureTypes.PublicSpace),
                NamingPanels: GetCount(FeatureTypes.NamingPanel),
                Total: total
            );
        }
        return resultList;
    }

    public async Task<(List<FeatureResult> features, int totalCount)> LoadAllFeaturesAsync(Guid userId, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadAllFeaturesAsync(conn, userId, skip, take, ct);
    }

    public async Task<(List<FeatureResult> features, int totalCount)> LoadByLayerAsync(Guid userId, string layer, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadByLayerAsync(conn, userId, layer, skip, take, ct);
    }
}
