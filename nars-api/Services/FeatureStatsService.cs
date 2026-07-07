using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class FeatureStatsService(AppDbContext db) : IFeatureStatsService
{
    public async Task<Dictionary<string, long>> GetFeatureCountsAsync(Guid userId, CancellationToken ct = default)
    {
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        var counts = new Dictionary<string, long>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            var count = await descriptor.GetDbSet(db)
                .Where(f => f.UserId == userId)
                .LongCountAsync(ct);
            counts[descriptor.Type] = count;
        }
        return counts;
    }

    public async Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds, CancellationToken ct = default)
    {
        if (userIds.Length == 0)
        {
            return [];
        }

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(ct);

        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        var featureCounts = new Dictionary<Guid, Dictionary<string, long>>();
        foreach (var user in users)
        {
            featureCounts[user.Id] = descriptors.ToDictionary(d => d.Type, _ => 0L);
        }

        foreach (var descriptor in descriptors)
        {
            var perUser = await descriptor.GetDbSet(db)
                .Where(f => userIds.Contains(f.UserId))
                .GroupBy(f => f.UserId)
                .Select(g => new { UserId = g.Key, Count = g.LongCount() })
                .ToListAsync(ct);

            foreach (var item in perUser)
            {
                if (featureCounts.TryGetValue(item.UserId, out var counts))
                {
                    counts[descriptor.Type] = item.Count;
                }
            }
        }

        var result = new Dictionary<Guid, UserFeatureStats>(users.Count);
        foreach (var user in users)
        {
            var counts = featureCounts[user.Id];
            long GetCount(string type) => counts.GetValueOrDefault(type, 0);
            var total = counts.Values.Sum();

            result[user.Id] = new UserFeatureStats(
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
        return result;
    }

    public async Task<(List<FeatureResult> features, int totalCount)> LoadAllFeaturesAsync(Guid userId, int skip, int take, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadAllFeaturesAsync(conn, userId, skip, take, ct);
    }

    public async Task<(List<FeatureResult> features, int totalCount)> LoadByLayerAsync(Guid userId, string layer, int skip, int take, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadByLayerAsync(conn, userId, layer, skip, take, ct);
    }
}
