using System.Data.Common;
using System.Text;
using System.Text.Json;
using NarsApi.DTOs;

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
    private static readonly string _loadFeaturesSql = BuildSql(withLayer: false);
    private static readonly string _loadByLayerSql = BuildSql(withLayer: true);

    private static string BuildSql(bool withLayer)
    {
        var cte = BuildUnionAllCte();
        var layerFilter = withLayer ? " AND layer = @layer" : "";
        return $"""
            WITH all_features AS ({cte}),
            filtered AS (
                SELECT id, label, data, created_at, layer, feature_type
                FROM all_features
                WHERE user_id = @uid{layerFilter}
            ),
            total AS (
                SELECT COUNT(*) AS total_count FROM filtered
            )
            SELECT f.id, f.label, f.data, f.created_at, f.layer, f.feature_type, t.total_count
            FROM filtered f, total t
            ORDER BY f.created_at
            OFFSET @skip LIMIT @take
            """;
    }

    private static string BuildUnionAllCte()
    {
        var sb = new StringBuilder();
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        for (var i = 0; i < descriptors.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine().Append("UNION ALL ");
            }

            sb.Append($"SELECT id, user_id, label, data, created_at, layer, '{descriptors[i].Type}' AS feature_type FROM {descriptors[i].TableName}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Loads features across all tables for a given user with pagination.
    /// Returns the feature rows and the total count (for pagination UI).
    /// </summary>
    public static async Task<(List<FeatureResult> features, int totalCount)> LoadAllFeaturesAsync(
        DbConnection conn,
        Guid userId,
        int skip,
        int take,
        CancellationToken ct = default) => await ExecuteQueryAsync(conn, _loadFeaturesSql, userId, layer: null, skip, take, ct);

    /// <summary>
    /// Loads features for a specific layer with pagination.
    /// Returns the feature rows and the total count (for pagination UI).
    /// </summary>
    public static async Task<(List<FeatureResult> features, int totalCount)> LoadByLayerAsync(
        DbConnection conn,
        Guid userId,
        string layer,
        int skip,
        int take,
        CancellationToken ct = default) => await ExecuteQueryAsync(conn, _loadByLayerSql, userId, layer, skip, take, ct);

    private static async Task<(List<FeatureResult> features, int totalCount)> ExecuteQueryAsync(
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

        SqlFragments.AddParam(cmd, "@uid", userId);
        if (layer is not null)
        {
            SqlFragments.AddParam(cmd, "@layer", layer);
        }

        SqlFragments.AddParam(cmd, "@skip", skip);
        SqlFragments.AddParam(cmd, "@take", take);

        var rows = new List<FeatureResult>();
        var totalCount = 0;

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
            var label = await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1);
            var dataJson = await reader.IsDBNullAsync(2, ct) ? "{}" : reader.GetString(2);
            var createdAt = reader.GetDateTime(3);
            var layerVal = await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4);
            var type = reader.GetString(5);
            totalCount = (int)reader.GetInt64(6);

            rows.Add(new FeatureResult(
                Id: id.ToString(),
                Type: type,
                Layer: layerVal,
                Label: label,
                Data: string.IsNullOrWhiteSpace(dataJson)
                    ? JsonDocument.Parse("{}").RootElement
                    : JsonSerializer.Deserialize<JsonElement>(dataJson),
                CreatedAt: createdAt.ToString("o")
            ));
        }

        return (rows, totalCount);
    }

    public static void AddParameter(DbCommand cmd, string name, Guid[] values)
        => SqlFragments.AddParam(cmd, name, values);
}
