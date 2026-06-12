using System.Data.Common;
using System.Text;
using System.Text.Json;

namespace NarsApi.Infrastructure;

/// <summary>
/// Shared ADO.NET query helper for loading features across all tables.
/// Eliminates ~100 lines of duplicated boilerplate between
/// FeaturesController and FeatureCatalogController.
/// UNION ALL branches are built from <see cref="FeatureTypeRegistry"/>
/// so new feature types are automatically included.
/// </summary>
public static class FeatureQueryHelper
{
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

    /// <summary>
    /// Loads features across all tables for a given user with pagination.
    /// Returns the feature rows and the total count (for pagination UI).
    /// </summary>
    public static async Task<(List<object> features, int totalCount)> LoadAllFeaturesAsync(
        DbConnection conn,
        Guid userId,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        return await ExecuteQueryAsync(conn, BuildLoadFeaturesSql(), userId, layer: null, skip, take, ct);
    }

    /// <summary>
    /// Loads features for a specific layer with pagination.
    /// Returns the feature rows and the total count (for pagination UI).
    /// </summary>
    public static async Task<(List<object> features, int totalCount)> LoadByLayerAsync(
        DbConnection conn,
        Guid userId,
        string layer,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        return await ExecuteQueryAsync(conn, BuildLoadByLayerSql(), userId, layer, skip, take, ct);
    }

    private static async Task<(List<object> features, int totalCount)> ExecuteQueryAsync(
        DbConnection conn,
        string sql,
        Guid userId,
        string? layer,
        int skip,
        int take,
        CancellationToken ct)
    {
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

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
