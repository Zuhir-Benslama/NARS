using System.Data.Common;
using System.Text.Json;

namespace NarsApi.Infrastructure;

/// <summary>
/// Shared ADO.NET query helper for loading features across all tables.
/// Eliminates ~100 lines of duplicated boilerplate between
/// FeaturesController and FeatureCatalogController.
/// </summary>
public static class FeatureQueryHelper
{
    // Verbatim string (no $ interpolation) — eliminates risk of accidental
    // user-input injection if a future developer modifies this query.
    private const string LoadFeaturesSql = """
        WITH all_features AS (
            SELECT id, user_id, label, data, created_at, layer, 'area' AS feature_type FROM areas
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'district' FROM districts
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'city_center' FROM city_centers
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'road' FROM roads
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'house_entrance' FROM house_entrances
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'public_building' FROM public_buildings
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'public_space' FROM public_spaces
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'naming_panel' FROM naming_panels
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

    private const string LoadByLayerSql = """
        WITH all_features AS (
            SELECT id, user_id, label, data, created_at, layer, 'area' AS feature_type FROM areas
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'district' FROM districts
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'city_center' FROM city_centers
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'road' FROM roads
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'house_entrance' FROM house_entrances
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'public_building' FROM public_buildings
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'public_space' FROM public_spaces
            UNION ALL SELECT id, user_id, label, data, created_at, layer, 'naming_panel' FROM naming_panels
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
        return await ExecuteQueryAsync(conn, LoadFeaturesSql, userId, layer: null, skip, take, ct);
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
        return await ExecuteQueryAsync(conn, LoadByLayerSql, userId, layer, skip, take, ct);
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
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
            await conn.OpenAsync(ct);

        try
        {
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
                var label = reader.IsDBNull(1) ? null : reader.GetString(1);
                var dataJson = reader.IsDBNull(2) ? "{}" : reader.GetString(2);
                var createdAt = reader.GetDateTime(3);
                var layerVal = reader.IsDBNull(4) ? null : reader.GetString(4);
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
        finally
        {
            if (!wasOpen && conn.State == System.Data.ConnectionState.Open)
                await conn.CloseAsync();
        }
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
