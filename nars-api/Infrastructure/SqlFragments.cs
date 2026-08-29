using System.Data;
using NarsApi.Models;

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
    /// Escapes PostgreSQL LIKE wildcards (<c>%</c>, <c>_</c>, <c>\</c>) in a
    /// user-supplied search term so they are matched literally rather than
    /// interpreted as pattern metacharacters. Single source of truth for all
    /// ILIKE search paths (locations, boundary lookups).
    /// </summary>
    internal static string EscapeLikeWildcards(string input) =>
        // Escape the escape character FIRST so a user-supplied backslash cannot
        // neutralize the escaping of a following % or _ (e.g. "50\%" must match
        // the literal text, not "50" + any single character).
        input.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// IN-clause for the urban area layers ('central_urban', 'secondary_urban').
    /// Sourced from <see cref="AreaLayers.Urban"/> so the SQL and the model
    /// cannot drift apart.
    /// </summary>
    internal static string UrbanAreaLayersSqlIn { get; } =
        string.Join(", ", FeatureTypes.AreaLayers.Urban.Select(l => $"'{l}'"));

    /// <summary>
    /// Template for reconstructing a valid PostGIS POLYGON from a feature's
    /// stored JSONB data. Use {0} as the table alias placeholder.
    /// Coordinates are stored as [{lat, lng}] and converted to GeoJSON [lng, lat] order.
    /// ST_MakeValid ensures legacy or edge-case geometries are handled gracefully.
    /// </summary>
    internal const string PolygonFromDataTemplate = @"
        ST_MakeValid(ST_SetSRID(ST_GeomFromGeoJSON(
            json_build_object(
                'type', 'Polygon',
                'coordinates', json_build_array((
                    SELECT json_agg(json_build_array(
                        (c->>'lng')::float, (c->>'lat')::float
                    ) ORDER BY ord)
                    FROM jsonb_array_elements({0}.data::jsonb->'coordinates')
                    WITH ORDINALITY AS t(c, ord)
                ))
            )::text
        ), 4326))";

    /// <summary>
    /// Template for reconstructing a valid PostGIS LINESTRING from a feature's
    /// stored JSONB data. Use {0} as the table alias placeholder.
    /// ST_MakeValid guards against degenerate linestrings.
    /// </summary>
    internal const string LineStringFromDataTemplate = @"
        ST_MakeValid(ST_SetSRID(ST_GeomFromGeoJSON(
            json_build_object(
                'type', 'LineString',
                'coordinates', (
                    SELECT json_agg(json_build_array(
                        (c->>'lng')::float, (c->>'lat')::float
                    ) ORDER BY ord)
                    FROM jsonb_array_elements({0}.data::jsonb->'coordinates')
                    WITH ORDINALITY AS t(c, ord)
                )
            )::text
        ), 4326))";

    /// <summary>
    /// Adds a named parameter to an ADO.NET command.
    /// Shared by NarsControllerBase and ScatteredAreaService to avoid duplication.
    /// </summary>
    internal static IDbDataParameter AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
        return p;
    }

    /// <summary>
    /// Returns the polygon SQL fragment with the given table alias substituted.
    /// Uses format-string templating instead of string.Replace to guarantee
    /// no accidental matches inside JSON string values or column names.
    /// </summary>
    internal static string PolygonFromDataWithAlias(string alias)
    {
        if (!IsValidAlias(alias))
        {
            throw new ArgumentException($"Invalid table alias '{alias}'. Only alphanumeric characters are allowed.", nameof(alias));
        }
        return string.Format(PolygonFromDataTemplate, alias);
    }

    /// <summary>
    /// Returns the linestring SQL fragment with the given table alias substituted.
    /// </summary>
    internal static string LineStringFromDataWithAlias(string alias)
    {
        if (!IsValidAlias(alias))
        {
            throw new ArgumentException($"Invalid table alias '{alias}'. Only alphanumeric characters are allowed.", nameof(alias));
        }
        return string.Format(LineStringFromDataTemplate, alias);
    }

    private static bool IsValidAlias(string alias) =>
        !string.IsNullOrWhiteSpace(alias) && alias.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Default alias ("f") variant for backward compatibility.
    /// New code should call PolygonFromDataWithAlias explicitly.
    /// </summary>
    internal static string PolygonFromData { get; } = string.Format(PolygonFromDataTemplate, "f");

    /// <summary>
    /// Default alias ("f") variant for backward compatibility.
    /// New code should call LineStringFromDataWithAlias explicitly.
    /// </summary>
    internal static string LineStringFromData { get; } = string.Format(LineStringFromDataTemplate, "f");
}
