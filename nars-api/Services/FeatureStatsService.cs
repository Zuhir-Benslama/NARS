using System.Data.Common;
using System.Text;
using System.Text.Json;
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
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        var sql = new StringBuilder();
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        for (var i = 0; i < descriptors.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(" UNION ALL ");
            }

            sql.Append($"SELECT '{descriptors[i].Type}' AS type, COUNT(*) FROM {descriptors[i].TableName} WHERE user_id = @uid");
        }
        cmd.CommandText = sql.ToString();
        SqlFragments.AddParam(cmd, "@uid", userId);

        var counts = new Dictionary<string, long>(descriptors.Count);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        return counts;
    }

    public async Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds, CancellationToken ct = default)
    {
        if (userIds.Length == 0)
        {
            return [];
        }

        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        var unionBuilder = new StringBuilder();
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        for (var i = 0; i < descriptors.Count; i++)
        {
            if (i > 0)
            {
                unionBuilder.Append(" UNION ALL ");
            }

            unionBuilder.Append($"SELECT id, user_id, '{descriptors[i].Type}' AS ft FROM {descriptors[i].TableName}");
        }

        var caseBuilder = new StringBuilder();
        for (var i = 0; i < descriptors.Count; i++)
        {
            caseBuilder.AppendLine($"                    COALESCE(SUM(CASE WHEN f.ft = '{descriptors[i].Type}' THEN 1 ELSE 0 END), 0),");
        }

        cmd.CommandText = $"""
            SELECT
                u.id,
                u.username,
                u.name,
                u.email,
                u.role,
                {caseBuilder}                    COUNT(f.id)
            FROM users u
            LEFT JOIN (
                {unionBuilder}
            ) f ON f.user_id = u.id
            WHERE u.id = ANY(@ids)
            GROUP BY u.id, u.username, u.name, u.email, u.role
            """;

        FeatureQueryHelper.AddParameter(cmd, "@ids", userIds);

        var typeColIndex = new Dictionary<string, int>(descriptors.Count);
        for (var i = 0; i < descriptors.Count; i++)
        {
            typeColIndex[descriptors[i].Type] = 5 + i;
        }

        var totalCol = 5 + descriptors.Count;

        var result = new Dictionary<Guid, UserFeatureStats>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            result[id] = new UserFeatureStats(
                UserId: id.ToString(),
                Username: reader.GetString(1),
                Name: reader.GetString(2),
                Email: reader.GetString(3),
                Role: reader.GetString(4),
                Areas: reader.GetInt64(typeColIndex[FeatureTypes.Area]),
                Districts: reader.GetInt64(typeColIndex[FeatureTypes.District]),
                CityCenters: reader.GetInt64(typeColIndex[FeatureTypes.CityCenter]),
                Roads: reader.GetInt64(typeColIndex[FeatureTypes.Road]),
                HouseEntrances: reader.GetInt64(typeColIndex[FeatureTypes.HouseEntrance]),
                PublicBuildings: reader.GetInt64(typeColIndex[FeatureTypes.PublicBuilding]),
                PublicSpaces: reader.GetInt64(typeColIndex[FeatureTypes.PublicSpace]),
                NamingPanels: reader.GetInt64(typeColIndex[FeatureTypes.NamingPanel]),
                Total: reader.GetInt64(totalCol)
            );
        }
        return result;
    }

    public async Task<(List<object> features, int totalCount)> LoadAllFeaturesAsync(Guid userId, int skip, int take, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadAllFeaturesAsync(conn, userId, skip, take, ct);
    }

    public async Task<(List<object> features, int totalCount)> LoadByLayerAsync(Guid userId, string layer, int skip, int take, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        return await FeatureQueryHelper.LoadByLayerAsync(conn, userId, layer, skip, take, ct);
    }
}
