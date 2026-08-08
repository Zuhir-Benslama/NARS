using System.Collections.Concurrent;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class ScatteredAreaService(
    IDbContextFactory<AppDbContext> dbFactory,
    IDateTimeProvider timeProvider,
    ILogger<ScatteredAreaService> logger)
    : IScatteredAreaService
{
    private const string DefaultLabel = "Scattered Area";
    private const string GenericErrorMessage = "An error occurred during scattered area recomputation.";

    // Error state keyed per (user, commune) so one account's failure is never
    // surfaced to another. Only a generic message is stored; the real exception
    // goes to the logger.
    private readonly ConcurrentDictionary<(Guid UserId, int CommuneId), (DateTimeOffset Timestamp, string Message)> _lastErrors = new();

    public (DateTimeOffset Timestamp, string Message)? GetLastError(Guid userId, int communeId)
        => _lastErrors.TryGetValue((userId, communeId), out var error) ? error : null;

    public async Task<bool> RefreshAsync(Guid userId, int communeId, CancellationToken cancellationToken = default)
    {
        _lastErrors.TryRemove((userId, communeId), out _);

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var conn = db.Database.GetDbConnection();

            string? scatteredGeoJson = null;
            await using var handle = await conn.EnsureOpenAsync(cancellationToken);

            var areaTable = FeatureTypeRegistry.ValidateTableName(
                FeatureTypeRegistry.GetDescriptor(FeatureTypes.Area)?.TableName ?? "areas");
            await using var cmd = conn.CreateCommand();
#pragma warning disable S2077 // Table name is allowlist-validated; parameters used for values
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
                      AND f.layer  IN ({SqlFragments.UrbanAreaLayersSqlIn})
                )
                SELECT ST_AsGeoJSON(
                    ST_Difference(
                        boundary.geom,
                        COALESCE(urban.geom, ST_GeomFromText('GEOMETRYCOLLECTION EMPTY', 4326))
                    ),
                    6
                )
                FROM boundary LEFT JOIN urban ON true";
#pragma warning restore S2077

            SqlFragments.AddParam(cmd, "@cid", communeId);
            SqlFragments.AddParam(cmd, "@uid", userId);

            // CommandBehavior.SequentialAccess streams the large GeoJSON column
            // instead of buffering it in Npgsql's internal buffer.
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            if (await reader.ReadAsync(cancellationToken) && !await reader.IsDBNullAsync(0, cancellationToken))
            {
                scatteredGeoJson = await reader.GetTextReader(0).ReadToEndAsync(cancellationToken);
            }

            if (scatteredGeoJson is null)
            {
                return true;
            }

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

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
                Label = DefaultLabel,
                Data = JsonSerializer.Serialize(new
                {
                    type = FeatureTypes.Area,
                    label = DefaultLabel,
                    layer = "scattered",
                    geometry = scatteredGeoJson,
                    coordinates,
                }),
            });
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastErrors[(userId, communeId)] = (timeProvider.UtcNow, GenericErrorMessage);

            logger.LogError(ex, "ScatteredAreaService refresh failed for user {UserId}, commune {CommuneId}", userId, communeId);
            return false;
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
        if (!geo.TryGetProperty("type", out var typeProp))
        {
            return result;
        }

        var geomType = typeProp.GetString();

        if (geomType == "Polygon" && geo.TryGetProperty("coordinates", out var coords))
        {
            result.Add(RingToLatLngList(coords.EnumerateArray().FirstOrDefault()));
        }
        else if (geomType == "MultiPolygon" && geo.TryGetProperty("coordinates", out var multiCoords))
        {
            foreach (var polygon in multiCoords.EnumerateArray())
            {
                result.Add(RingToLatLngList(polygon.EnumerateArray().FirstOrDefault()));
            }
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
            {
                points.Add(new { lng = arr[0].GetDouble(), lat = arr[1].GetDouble() });
            }
        }
        return points;
    }
}
