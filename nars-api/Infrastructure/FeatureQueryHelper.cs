using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    // JsonSerializer.Deserialize<JsonElement> returns an unowned element backed
    // by the serializer's own buffer — unlike JsonDocument.Parse(...).RootElement,
    // it requires no disposal and cannot leak the pooled document.
    private static readonly JsonElement EmptyJsonObject = JsonSerializer.Deserialize<JsonElement>("{}");
    private static readonly string _loadFeaturesSql = BuildSql(withLayer: false);
    private static readonly string _loadByLayerSql = BuildSql(withLayer: true);

    private static string BuildSql(bool withLayer)
    {
        var cte = BuildUnionAllCte();
        var layerFilter = withLayer ? " AND layer = @layer" : "";
        // Two result sets so total_count is correct even when OFFSET lands past
        // the last row (a cross join of the count onto feature rows would emit
        // zero rows — and therefore report a total of 0 — on an empty page).
        // The CTE is repeated because its scope ends at each statement boundary.
        return $"""
            WITH all_features AS ({cte}),
            filtered AS (
                SELECT id, label, data, created_at, layer, feature_type
                FROM all_features
                WHERE user_id = @uid{layerFilter}
            )
            SELECT COUNT(*) AS total_count FROM filtered;
            WITH all_features AS ({cte}),
            filtered AS (
                SELECT id, label, data, created_at, layer, feature_type
                FROM all_features
                WHERE user_id = @uid{layerFilter}
            )
            SELECT id, label, data, created_at, layer, feature_type
            FROM filtered
            ORDER BY created_at, id
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
        ILogger? logger = null,
        CancellationToken ct = default) => await ExecuteQueryAsync(conn, _loadFeaturesSql, userId, layer: null, skip, take, logger, ct);

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
        ILogger? logger = null,
        CancellationToken ct = default) => await ExecuteQueryAsync(conn, _loadByLayerSql, userId, layer, skip, take, logger, ct);

    private static async Task<(List<FeatureResult> features, int totalCount)> ExecuteQueryAsync(
        DbConnection conn,
        string sql,
        Guid userId,
        string? layer,
        int skip,
        int take,
        ILogger? logger,
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

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        // First result set: the total matching row count (independent of paging).
        await reader.ReadAsync(ct);
        var totalOrdinal = reader.GetOrdinal("total_count");
        var totalCount = Convert.ToInt32(reader.GetInt64(totalOrdinal));

        // Second result set: the paged feature rows.
        await reader.NextResultAsync(ct);
        var idOrdinal = reader.GetOrdinal("id");
        var labelOrdinal = reader.GetOrdinal("label");
        var dataOrdinal = reader.GetOrdinal("data");
        var createdAtOrdinal = reader.GetOrdinal("created_at");
        var layerOrdinal = reader.GetOrdinal("layer");
        var typeOrdinal = reader.GetOrdinal("feature_type");

        while (await reader.ReadAsync(ct))
        {
            var idValue = reader.GetValue(idOrdinal);
            Guid id = idValue switch
            {
                Guid g => g,
                string s => Guid.TryParse(s, out var parsed) ? parsed
                    : throw new InvalidOperationException($"Invalid GUID in database: {s}"),
                _ => throw new InvalidOperationException($"Unexpected ID type: {idValue?.GetType().Name}")
            };
            var label = await reader.IsDBNullAsync(labelOrdinal, ct) ? null : reader.GetString(labelOrdinal);
            var dataJson = await reader.IsDBNullAsync(dataOrdinal, ct) ? "{}" : reader.GetString(dataOrdinal);
            var createdAt = reader.GetDateTime(createdAtOrdinal);
            var layerVal = await reader.IsDBNullAsync(layerOrdinal, ct) ? null : reader.GetString(layerOrdinal);
            var type = reader.GetString(typeOrdinal);

            var data = EmptyJsonObject;
            if (!string.IsNullOrWhiteSpace(dataJson))
            {
                try
                {
                    data = JsonSerializer.Deserialize<JsonElement>(dataJson);
                }
                catch (JsonException ex)
                {
                    // Corrupt stored data must not take the endpoint down; degrade
                    // gracefully but log so operators can locate the bad row.
                    logger?.LogWarning(ex,
                        "Corrupt feature data JSON for feature {FeatureId} (type {FeatureType}); returning empty object",
                        id, type);
                }
            }

            rows.Add(new FeatureResult(
                Id: id.ToString(),
                Type: type,
                Layer: layerVal,
                Label: label,
                Data: data,
                CreatedAt: createdAt.ToString(JsonHelper.IsoDateFormat)
            ));
        }

        return (rows, totalCount);
    }
}
