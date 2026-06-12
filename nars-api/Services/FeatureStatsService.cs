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
        for (int i = 0; i < descriptors.Count; i++)
        {
            if (i > 0) sql.Append(" UNION ALL ");
            sql.Append($"SELECT '{descriptors[i].Type}' AS type, COUNT(*) FROM {descriptors[i].TableName} WHERE user_id = @uid");
        }
        cmd.CommandText = sql.ToString();
        var uidParam = cmd.CreateParameter();
        uidParam.ParameterName = "@uid";
        uidParam.Value = userId;
        cmd.Parameters.Add(uidParam);

        var counts = new Dictionary<string, long>(descriptors.Count);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.GetString(0)] = reader.GetInt64(1);

        return counts;
    }

    public async Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds, CancellationToken ct = default)
    {
        if (userIds.Length == 0) return new Dictionary<Guid, UserFeatureStats>();

        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        var unionBuilder = new StringBuilder();
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        for (int i = 0; i < descriptors.Count; i++)
        {
            if (i > 0) unionBuilder.Append(" UNION ALL ");
            unionBuilder.Append($"SELECT id, user_id, '{descriptors[i].Type}' AS ft FROM {descriptors[i].TableName}");
        }

        var caseBuilder = new StringBuilder();
        for (int i = 0; i < descriptors.Count; i++)
            caseBuilder.AppendLine($"                    COALESCE(SUM(CASE WHEN f.ft = '{descriptors[i].Type}' THEN 1 ELSE 0 END), 0),");

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

        var param = cmd.CreateParameter();
        param.ParameterName = "@ids";
        param.Value = userIds;
        cmd.Parameters.Add(param);

        var typeColIndex = new Dictionary<string, int>(descriptors.Count);
        for (int i = 0; i < descriptors.Count; i++)
            typeColIndex[descriptors[i].Type] = 5 + i;
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
        return await ExecuteQueryAsync(BuildLoadFeaturesSql(), userId, null, skip, take, ct);
    }

    public async Task<(List<object> features, int totalCount)> LoadByLayerAsync(Guid userId, string layer, int skip, int take, CancellationToken ct = default)
    {
        return await ExecuteQueryAsync(BuildLoadByLayerSql(), userId, layer, skip, take, ct);
    }

    private async Task<(List<object> features, int totalCount)> ExecuteQueryAsync(
        string sql, Guid userId, string? layer, int skip, int take, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 30;

        AddParameter(cmd, "@uid", userId);
        if (layer is not null)
            AddParameter(cmd, "@layer", layer);
        AddParameter(cmd, "@skip", skip);
        AddParameter(cmd, "@take", take);

        var rows = new List<object>();
        int totalCount = 0;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var idValue = reader.GetValue(0);
            Guid id = idValue switch
            {
                Guid g => g,
                string s => Guid.Parse(s),
                _ => throw new InvalidOperationException($"Unexpected ID type: {idValue?.GetType().Name}")
            };
            var label = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
            var dataJson = await reader.IsDBNullAsync(2) ? "{}" : reader.GetString(2);
            var createdAt = reader.GetDateTime(3);
            var layerVal = await reader.IsDBNullAsync(4) ? null : reader.GetString(4);
            var type = reader.GetString(5);
            totalCount = (int)reader.GetInt64(6);

            rows.Add(new
            {
                id = id.ToString(),
                type,
                layer = layerVal,
                label,
                data = string.IsNullOrWhiteSpace(dataJson)
                    ? JsonDocument.Parse("{}").RootElement
                    : JsonSerializer.Deserialize<JsonElement>(dataJson),
                created_at = createdAt.ToString("o"),
            });
        }

        return (rows, totalCount);
    }

    private static string BuildUnionAllCte()
    {
        var sb = new StringBuilder();
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        for (int i = 0; i < descriptors.Count; i++)
        {
            if (i > 0) sb.AppendLine().Append("UNION ALL ");
            sb.Append($"SELECT id, user_id, label, data, created_at, layer, '{descriptors[i].Type}' AS feature_type FROM {descriptors[i].TableName}");
        }
        return sb.ToString();
    }

    private static string BuildLoadFeaturesSql() =>
        $"""
        WITH all_features AS (
            {BuildUnionAllCte()}
        ),
        filtered AS (
            SELECT id, label, data, created_at, layer, feature_type
            FROM all_features
            WHERE user_id = @uid
        ),
        total AS (
            SELECT COUNT(*) AS total_count FROM filtered
        )
        SELECT f.id, f.label, f.data, f.created_at, f.layer, f.feature_type, t.total_count
        FROM filtered f, total t
        ORDER BY f.created_at
        OFFSET @skip LIMIT @take
        """;

    private static string BuildLoadByLayerSql() =>
        $"""
        WITH all_features AS (
            {BuildUnionAllCte()}
        ),
        filtered AS (
            SELECT id, label, data, created_at, layer, feature_type
            FROM all_features
            WHERE user_id = @uid AND layer = @layer
        ),
        total AS (
            SELECT COUNT(*) AS total_count FROM filtered
        )
        SELECT f.id, f.label, f.data, f.created_at, f.layer, f.feature_type, t.total_count
        FROM filtered f, total t
        ORDER BY f.created_at
        OFFSET @skip LIMIT @take
        """;

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
