namespace NarsApi.Infrastructure;

/// <summary>
/// Shared PostGIS SQL fragment constants used by ValidationController and
/// FeaturesController.  A single source of truth prevents the two locations
/// from drifting (e.g. one missing ST_MakeValid — see security issue #4).
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
}
