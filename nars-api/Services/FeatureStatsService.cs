using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class FeatureStatsService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<FeatureStatsService>? logger = null) : IFeatureStatsService
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

        var sql = BuildUnionAll((i, table) =>
            $"SELECT @t{i} AS Type, COUNT(*)::bigint AS Count FROM {table} WHERE user_id = @u{i}");

        await using var cmd = new NpgsqlCommand(sql, conn);
        var paramIndex = 0;
        foreach (var type in _featureTypes)
        {
            cmd.Parameters.Add(new NpgsqlParameter<string>($"t{paramIndex}", type));
            cmd.Parameters.Add(new NpgsqlParameter<Guid>($"u{paramIndex}", userId));
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
            .Select(u => new { u.Id, u.Username, u.Name, u.Email, u.Role })
            .ToListAsync(ct);

        // If none of the requested user IDs matched a real user, there is nothing
        // to count — skip the UNION ALL round-trip entirely.
        if (users.Count == 0)
        {
            return [];
        }

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var connHandle = await conn.EnsureOpenAsync(ct);

        // Build a single UNION ALL query across all tables, grouped by user_id.
        // Uses the matched user IDs (not the full request array) so IDs that
        // resolved to no user are never scanned.
        var matchedIds = users.Select(u => u.Id).ToArray();
        var sql = BuildUnionAll((i, table) =>
            $"SELECT user_id, @tp{i} AS Type, COUNT(*)::bigint AS Count FROM {table} WHERE user_id = ANY(@u{i}) GROUP BY user_id");

        await using var cmd = new NpgsqlCommand(sql, conn);
        var paramIndex = 0;
        foreach (var type in _featureTypes)
        {
            cmd.Parameters.Add(new NpgsqlParameter<string>($"tp{paramIndex}", type));
            cmd.Parameters.Add(new NpgsqlParameter<Guid[]>($"u{paramIndex}", matchedIds));
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

    public async Task<(List<FeatureResult> features, long totalCount)> LoadAllFeaturesAsync(Guid userId, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadAllFeaturesAsync(conn, userId, skip, take, logger, ct);
    }

    public async Task<(List<FeatureResult> features, long totalCount)> LoadByLayerAsync(Guid userId, string layer, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadByLayerAsync(conn, userId, layer, skip, take, logger, ct);
    }

    private static string BuildUnionAll(Func<int, string, string> branchBuilder)
    {
        var sb = new StringBuilder();
        var i = 0;
        foreach (var type in _featureTypes)
        {
            var descriptor = FeatureTypeRegistry.GetDescriptor(type);
            if (descriptor is null)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.AppendLine(" UNION ALL");
            }

            var tableName = FeatureTypeRegistry.ValidateTableName(descriptor.TableName);
            sb.Append(branchBuilder(i, tableName));
            i++;
        }
        return sb.ToString();
    }
}
