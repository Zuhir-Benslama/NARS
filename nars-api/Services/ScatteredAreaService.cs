using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Recomputes the scattered area geometry for a given user/commune pair and
/// persists the result to the features table.
///
/// The scattered area is defined as the commune boundary minus the union of all
/// urban area polygons drawn by the user. It is recomputed asynchronously
/// (fire-and-forget) after any urban area is saved or deleted.
///
/// Uses <see cref="IDbContextFactory{TContext}"/> so each call owns an
/// independent <see cref="AppDbContext"/> — it never borrows the request-scoped
/// context, which may be disposed before the task completes.
/// </summary>
public interface IScatteredAreaService
{
    /// <summary>
    /// Triggers an async recompute. The returned <see cref="Task"/> is intended
    /// for fire-and-forget: callers should use <c>_ = service.RefreshAsync(…)</c>
    /// and not await it on the request path.
    /// </summary>
    Task RefreshAsync(Guid userId, int communeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The timestamp and message of the most recent refresh failure, or null
    /// if the last refresh succeeded (or none has run yet).
    /// </summary>
    (DateTimeOffset Timestamp, string Message)? LastError { get; }
}

public sealed class ScatteredAreaService(IDbContextFactory<AppDbContext> dbFactory, ILogger<ScatteredAreaService> logger)
    : IScatteredAreaService
{
    private readonly object _errorLock = new();
    private (DateTimeOffset Timestamp, string Message)? _lastError;

    public (DateTimeOffset Timestamp, string Message)? LastError
    {
        get { lock (_errorLock) return _lastError; }
        private set { lock (_errorLock) _lastError = value; }
    }

    public async Task RefreshAsync(Guid userId, int communeId, CancellationToken cancellationToken = default)
    {
        LastError = null; // Clear any previous error
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var conn = db.Database.GetDbConnection();

            string? scatteredGeoJson = null;
            await using var handle = await conn.EnsureOpenAsync(cancellationToken);

            var areaTable = FeatureTypeRegistry.GetDescriptor(FeatureTypes.Area)?.TableName ?? "areas";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                WITH
                boundary AS (
                    SELECT geometry AS geom
                    FROM communes_boundaries
                    WHERE commune_id = @cid
                ),
                urban AS (
                    SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                    FROM {areaTable} f
                    WHERE f.user_id = @uid
                      AND f.layer  IN ('central_urban', 'secondary_urban')
                )
                SELECT ST_AsGeoJSON(
                    ST_Difference(
                        boundary.geom,
                        COALESCE(urban.geom, ST_GeomFromText('GEOMETRYCOLLECTION EMPTY', 4326))
                    ),
                    6
                )
                FROM boundary LEFT JOIN urban ON true";

            SqlFragments.AddParam(cmd, "@cid", communeId);
            SqlFragments.AddParam(cmd, "@uid", userId);

            // CommandBehavior.SequentialAccess streams the large GeoJSON column
            // instead of buffering it in Npgsql's internal buffer.
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            if (await reader.ReadAsync(cancellationToken) && !await reader.IsDBNullAsync(0, cancellationToken))
                scatteredGeoJson = await reader.GetTextReader(0).ReadToEndAsync(cancellationToken);

            if (scatteredGeoJson is null) return;

            await db.Areas
                .Where(f => f.UserId == userId &&
                            f.Layer == FeatureTypes.AreaLayers.Scattered)
                .ExecuteDeleteAsync(cancellationToken);

            // Extract per-polygon coordinate rings for SqlFragments.PolygonFromData
            // compatibility (reads f.data::jsonb->'coordinates').
            // For MultiPolygon results (common with Algerian communes), ALL rings are
            // stored so PostGIS spatial queries see the complete scattered geometry.
            // The full GeoJSON is also kept in 'geometry' for frontend rendering.
            using var geoDoc = JsonDocument.Parse(scatteredGeoJson);
            var allRings = ExtractAllRings(geoDoc.RootElement);
            // coordinates = flat list of rings; for a simple Polygon this is a
            // single-element list, for MultiPolygon it has one ring per polygon.
            var coordinates = allRings.Count == 1 ? (object)allRings[0] : allRings;

            db.Areas.Add(new Area
            {
                UserId = userId,
                Layer = FeatureTypes.AreaLayers.Scattered,
                Label = "Scattered Area",
                Data = JsonSerializer.Serialize(new
                {
                    type = "areas",
                    label = "Scattered Area",
                    layer = "scattered",
                    geometry = scatteredGeoJson,
                    coordinates = coordinates,
                }),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LastError = (DateTimeOffset.UtcNow, ex.Message);
            logger.LogError(ex, "ScatteredAreaService refresh failed for user {UserId}, commune {CommuneId}", userId, communeId);
        }
    }

    /// <summary>
    /// Extracts polygon coordinates from a GeoJSON geometry as a list of
    /// {lat, lng} ring arrays — the format expected by
    /// <see cref="SqlFragments.PolygonFromData"/>.
    ///
    /// For Polygon: returns the outer ring.
    /// For MultiPolygon: returns the outer rings of ALL constituent polygons
    /// so that spatial queries see the complete scattered geometry, not just
    /// the first disconnected piece (which is the common case for Algerian communes).
    /// </summary>
    private static List<List<object>> ExtractAllRings(JsonElement geo)
    {
        var result = new List<List<object>>();
        if (!geo.TryGetProperty("type", out var typeProp)) return result;

        var geomType = typeProp.GetString();

        if (geomType == "Polygon" && geo.TryGetProperty("coordinates", out var coords))
        {
            result.Add(RingToLatLngList(coords.EnumerateArray().FirstOrDefault()));
        }
        else if (geomType == "MultiPolygon" && geo.TryGetProperty("coordinates", out var multiCoords))
        {
            foreach (var polygon in multiCoords.EnumerateArray())
                result.Add(RingToLatLngList(polygon.EnumerateArray().FirstOrDefault()));
        }

        return result;
    }

    private static List<object> RingToLatLngList(JsonElement ring)
    {
        var points = new List<object>();
        foreach (var point in ring.EnumerateArray())
        {
            var arr = point.EnumerateArray().ToArray();
            if (arr.Length >= 2)
                points.Add(new { lng = arr[0].GetDouble(), lat = arr[1].GetDouble() });
        }
        return points;
    }
}
