using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class FeatureStatsService(IDbContextFactory<AppDbContext> dbFactory) : IFeatureStatsService
{
    private static readonly string[] _featureTypes =
    [
        FeatureTypes.Area, FeatureTypes.District, FeatureTypes.CityCenter, FeatureTypes.Road,
        FeatureTypes.HouseEntrance, FeatureTypes.PublicBuilding, FeatureTypes.PublicSpace, FeatureTypes.NamingPanel,
    ];

    public async Task<Dictionary<string, long>> GetFeatureCountsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var connHandle = await conn.EnsureOpenAsync(ct);

        var sb = new StringBuilder();
        var paramIndex = 0;
        foreach (var type in _featureTypes)
        {
            var descriptor = FeatureTypeRegistry.GetDescriptor(type);
            if (descriptor is null) continue;

            if (sb.Length > 0) sb.AppendLine(" UNION ALL");
            sb.Append($"SELECT @t{paramIndex} AS Type, COUNT(*)::bigint AS Count FROM {descriptor.TableName} WHERE user_id = @u{paramIndex}");
            paramIndex++;
        }

        await using var cmd = new NpgsqlCommand(sb.ToString(), conn);
        paramIndex = 0;
        foreach (var type in _featureTypes)
        {
            cmd.Parameters.AddWithValue($"t{paramIndex}", type);
            cmd.Parameters.AddWithValue($"u{paramIndex}", userId);
            paramIndex++;
        }

        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }
        return result;
    }

    public async Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds, CancellationToken ct = default)
    {
        if (userIds.Length == 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(ct);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var connHandle = await conn.EnsureOpenAsync(ct);

        // Build a single UNION ALL query across all tables, grouped by user_id.
        var sb = new StringBuilder();
        var paramIndex = 0;
        foreach (var type in _featureTypes)
        {
            var descriptor = FeatureTypeRegistry.GetDescriptor(type);
            if (descriptor is null) continue;

            if (sb.Length > 0) sb.AppendLine(" UNION ALL");
            sb.Append($"SELECT user_id, '{type}' AS Type, COUNT(*)::bigint AS Count FROM {descriptor.TableName} WHERE user_id = ANY(@u{paramIndex}) GROUP BY user_id");
            paramIndex++;
        }

        await using var cmd = new NpgsqlCommand(sb.ToString(), conn);
        paramIndex = 0;
        foreach (var _ in _featureTypes)
        {
            cmd.Parameters.AddWithValue($"u{paramIndex}", userIds);
            paramIndex++;
        }

        // Parse per-user-per-type counts from the single round-trip.
        var counts = users.ToDictionary(u => u.Id, _ => _featureTypes.ToDictionary(t => t, _ => 0L, StringComparer.Ordinal));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var uid = reader.GetGuid(0);
            var type = reader.GetString(1);
            var count = reader.GetInt64(2);
            if (counts.TryGetValue(uid, out var userCounts))
            {
                userCounts[type] = count;
            }
        }

        var resultList = new Dictionary<Guid, UserFeatureStats>(users.Count);
        foreach (var user in users)
        {
            var userCounts = counts[user.Id];
            long GetCount(string type) => userCounts.GetValueOrDefault(type, 0);
            var total = userCounts.Values.Sum();

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
