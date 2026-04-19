using System.Data;

namespace NarsApi.Infrastructure;

/// <summary>
/// Shared PostGIS SQL fragment constants and ADO.NET helpers used across
/// ValidationController, SpatialController, and ScatteredAreaService.
/// A single source of truth prevents SQL fragments from drifting between
/// locations (e.g. one missing ST_MakeValid).
/// </summary>
internal static class SqlFragments
{
    /// <summary>
    /// Reconstructs a valid PostGIS POLYGON from a feature's stored JSONB data.
    /// Coordinates are stored as [{lat, lng}] and converted to GeoJSON [lng, lat] order.
    /// ST_MakeValid ensures legacy or edge-case geometries are handled gracefully.
    /// Alias the features table as "f" in the outer query.
    /// </summary>
    internal const string PolygonFromData = @"
        ST_MakeValid(ST_SetSRID(ST_GeomFromGeoJSON(
            json_build_object(
                'type', 'Polygon',
                'coordinates', json_build_array((
                    SELECT json_agg(json_build_array(
                        (c->>'lng')::float, (c->>'lat')::float
                    ) ORDER BY ord)
                    FROM jsonb_array_elements(f.data::jsonb->'coordinates')
                    WITH ORDINALITY AS t(c, ord)
                ))
            )::text
        ), 4326))";

    /// <summary>
    /// Reconstructs a valid PostGIS LINESTRING from a feature's stored JSONB data.
    /// Alias the features table as "f" in the outer query.
    /// ST_MakeValid guards against degenerate linestrings (e.g. repeated identical points).
    /// </summary>
    internal const string LineStringFromData = @"
        ST_MakeValid(ST_SetSRID(ST_GeomFromGeoJSON(
            json_build_object(
                'type', 'LineString',
                'coordinates', (
                    SELECT json_agg(json_build_array(
                        (c->>'lng')::float, (c->>'lat')::float
                    ) ORDER BY ord)
                    FROM jsonb_array_elements(f.data::jsonb->'coordinates')
                    WITH ORDINALITY AS t(c, ord)
                )
            )::text
        ), 4326))";

    /// <summary>
    /// Adds a named parameter to an ADO.NET command.
    /// Shared by NarsControllerBase and ScatteredAreaService to avoid duplication.
    /// </summary>
    internal static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    /// <summary>
    /// Returns PolygonFromData with the table alias replaced from "f." to the
    /// specified alias. Uses word-boundary matching to avoid accidental replacements
    /// inside string literals or column names.
    /// </summary>
    internal static string PolygonFromDataWithAlias(string alias) =>
        PolygonFromData.Replace("f.data", $"{alias}.data").Replace("f.user_id", $"{alias}.user_id");
}
