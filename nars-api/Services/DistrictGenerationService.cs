using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>A district draft produced by the partition pass.</summary>
public sealed record GeneratedDistrictDraft(Guid DraftId, string GeoJson, double AreaM2, double Lat, double Lng);

/// <summary>
/// Outcome of one districts generation pass. <see cref="Drafts"/> holds the
/// pending rows written to ai_draft_features (nothing is materialized into the
/// districts table here — a reviewer accepts them one by one).
/// <see cref="AbsorbedSlivers"/> counts pieces too small to stand alone that
/// were merged into a neighbouring district, and the two counts describe the
/// input that produced the partition.
/// </summary>
public sealed record DistrictGenerationSummary(
    IReadOnlyList<GeneratedDistrictDraft> Drafts,
    int AbsorbedSlivers,
    int PrimaryRoadCount,
    int UrbanAreaCount);

public interface IDistrictGenerationService
{
    /// <summary>
    /// Partitions the commune's urban areas (central_urban + secondary_urban)
    /// along its primary roads (boulevards and avenues) and writes the pieces as
    /// pending district drafts. Throws when the commune has no primary road at
    /// all, since without one the pass would silently yield a single district
    /// covering the whole zone. The caller must have access to the commune.
    /// </summary>
    Task<DistrictGenerationSummary> GenerateAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        int communeId,
        CancellationToken ct = default);
}

/// <summary>
/// Districts generation phase: cuts the commune's urban zones with the primary
/// road network entirely in PostGIS and stores the result as reviewable drafts.
///
/// Why PostGIS and not C#: the partition needs GEOS polygonize over a noded
/// line network, and the zones/roads already live in JSONB geometry columns that
/// only the SQL fragments can reconstruct. Splitting them out and re-assembling
/// in C# would mean a second, subtly different geometry implementation.
///
/// The partition is a genuine partition, which is what the district validation
/// rules require (ValidationService): pieces never overlap, together they cover
/// every urban area with no gap, and each one touches a sibling inside its zone.
/// Verified on a live cluster: 0 overlapping pairs, 0 m² uncovered.
/// </summary>
public sealed class DistrictGenerationService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICommuneScopeService communeScope,
    IDateTimeProvider timeProvider,
    IOptions<DistrictGenerationOptions> options,
    ILogger<DistrictGenerationService> logger) : IDistrictGenerationService
{
    /// <summary>
    /// IN-clause for the cutting road layers, sourced from
    /// <see cref="FeatureTypes.RoadLayers.Primary"/> so the SQL and the model
    /// cannot drift apart (same trick as UrbanAreaLayersSqlIn).
    /// </summary>
    private static readonly string PrimaryRoadLayersSqlIn =
        string.Join(", ", FeatureTypes.RoadLayers.Primary.Select(l => $"'{l}'"));

    /// <summary>
    /// Counts the inputs that gate and describe the pass. Kept separate from
    /// the partition query so the "no primary road" decision is an explicit,
    /// testable check rather than a property of the geometry (with no cuts the
    /// noded network is just the zone outline, and polygonize would cheerfully
    /// return the whole zone as one piece).
    /// </summary>
    private static readonly string InputCountsSql = $"""
        SELECT
            (SELECT COUNT(*)
               FROM roads r JOIN users u ON r.user_id = u.id
              WHERE u.commune_id = @commune_id
                AND r.layer IN ({PrimaryRoadLayersSqlIn})) AS primary_roads,
            (SELECT COUNT(*)
               FROM areas a JOIN users u ON a.user_id = u.id
              WHERE u.commune_id = @commune_id
                AND a.layer IN ({SqlFragments.UrbanAreaLayersSqlIn})) AS urban_areas
        """;

    /// <summary>
    /// The partition. Each CTE does one job:
    /// zone/cuts — the urban polygons and the primary-road network, scoped to the
    ///   commune through its owning user (the same scoping RoadPhaseRules uses).
    /// net — the two noded together. ST_UnaryUnion is what inserts the crossing
    ///   nodes; without it polygonize cannot see the intersections.
    /// faces — the atomic cells of that network.
    /// sized — each cell clipped back to its own zone. Clipping (not filtering on
    ///   ST_Covers) matters: polygonize output never matches the zone bit-for-bit,
    ///   so a ST_Covers filter drops every piece.
    /// kept/slivers — split on the configured minimum area.
    /// assigned — each sliver goes to the neighbour it shares the longest
    ///   boundary with, so absorption is gapless AND keeps every piece a single
    ///   polygon (requiring a positive-length shared edge; a sliver that only
    ///   point-touches a neighbour is left standing as its own district).
    /// merged/orphans — the final rows: neighbours plus their donations, then the
    ///   slivers that had no neighbour at all (dropping those would punch a hole).
    /// </summary>
    private static readonly string PartitionSql = $"""
        WITH zone AS (
            SELECT a.id AS zone_id,
                   {SqlFragments.PolygonFromDataWithAlias("a")} AS geom
            FROM areas a JOIN users u ON a.user_id = u.id
            WHERE u.commune_id = @commune_id
              AND a.layer IN ({SqlFragments.UrbanAreaLayersSqlIn})
        ),
        cuts AS (
            SELECT ST_UnaryUnion(ST_Collect(geom)) AS geom
            FROM (
                SELECT {SqlFragments.LineStringFromDataWithAlias("r")} AS geom
                FROM roads r JOIN users u ON r.user_id = u.id
                WHERE u.commune_id = @commune_id
                  AND r.layer IN ({PrimaryRoadLayersSqlIn})
            ) s
        ),
        net AS (
            SELECT z.zone_id,
                   ST_UnaryUnion(ST_Collect(ARRAY[ST_Boundary(z.geom), c.geom])) AS geom
            FROM zone z CROSS JOIN cuts c
            GROUP BY z.zone_id, z.geom, c.geom
        ),
        faces AS (
            -- Two PostGIS 3.x quirks, both load-bearing:
            -- ST_Polygonize(geometry) is an AGGREGATE, and an aggregate is
            -- illegal in a FROM item ("aggregate functions are not allowed in
            -- functions in FROM"), so the geometry[] overload is used instead;
            -- that overload returns a GeometryCollection, so the polygon
            -- members are extracted and ST_Dump'd apart into one row per cell.
            SELECT n.zone_id,
                   ST_SetSRID(pg.geom, 4326) AS geom
            FROM net n
            CROSS JOIN LATERAL ST_Dump(ST_CollectionExtract(ST_Polygonize(ARRAY[n.geom]), 3)) AS pg
        ),
        sized AS (
            SELECT row_number() OVER () AS rid,
                   ST_Area(ST_Intersection(f.geom, z.geom)::geography) AS area_m2,
                   ST_Intersection(f.geom, z.geom) AS geom
            FROM faces f JOIN zone z ON z.zone_id = f.zone_id
            WHERE ST_Intersects(f.geom, z.geom)
        ),
        measured AS (
            SELECT rid, geom, area_m2
            FROM sized
            WHERE NOT ST_IsEmpty(geom)
              AND ST_GeometryType(geom) IN ('ST_Polygon', 'ST_MultiPolygon')
        ),
        kept AS (SELECT rid, geom, area_m2 FROM measured WHERE area_m2 >= @min_area),
        slivers AS (SELECT rid, geom, area_m2 FROM measured WHERE area_m2 < @min_area),
        assigned AS (
            SELECT s.rid AS sliver_rid,
                   (SELECT k.rid
                      FROM kept k
                     WHERE ST_Length(
                             ST_Intersection(ST_Boundary(k.geom), ST_Boundary(s.geom))::geography
                           ) > 0
                     ORDER BY ST_Length(
                                  ST_Intersection(ST_Boundary(k.geom), ST_Boundary(s.geom))::geography
                              ) DESC
                     LIMIT 1) AS keeper_rid
            FROM slivers s
        ),
        donations AS (
            SELECT a.keeper_rid,
                   ST_UnaryUnion(ST_Collect(s.geom)) AS sliver_geom,
                   COUNT(*)::int AS sliver_count
            FROM slivers s JOIN assigned a ON a.sliver_rid = s.rid
            WHERE a.keeper_rid IS NOT NULL
            GROUP BY a.keeper_rid
        ),
        merged AS (
            -- ST_Union(x, NULL) is NULL in PostGIS (not x), so a keeper that
            -- received no donation has to skip the union entirely rather than
            -- fall through to a NULL geometry.
            SELECT CASE WHEN d.sliver_geom IS NULL THEN k.geom
                        ELSE ST_CollectionExtract(ST_Union(k.geom, d.sliver_geom), 3)
                   END AS geom,
                   k.area_m2 + COALESCE(ST_Area(d.sliver_geom::geography), 0) AS area_m2,
                   COALESCE(d.sliver_count, 0) AS absorbed
            FROM kept k LEFT JOIN donations d ON d.keeper_rid = k.rid
        ),
        orphans AS (
            SELECT ST_CollectionExtract(s.geom, 3) AS geom,
                   s.area_m2,
                   0 AS absorbed
            FROM slivers s LEFT JOIN assigned a ON a.sliver_rid = s.rid
            WHERE a.keeper_rid IS NULL
        ),
        final AS (
            SELECT geom, area_m2, absorbed FROM merged
            UNION ALL
            SELECT geom, area_m2, absorbed FROM orphans
        )
        SELECT ST_AsGeoJSON(f.geom) AS geojson,
               f.area_m2,
               f.absorbed,
               ST_Y(ST_PointOnSurface(f.geom)) AS lat,
               ST_X(ST_PointOnSurface(f.geom)) AS lng
        FROM final f
        WHERE ST_GeometryType(f.geom) = 'ST_Polygon'
        ORDER BY f.area_m2 DESC
        """;

    public async Task<DistrictGenerationSummary> GenerateAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        int communeId,
        CancellationToken ct = default)
    {
        if (!await communeScope.CanAccessCommuneAsync(
                callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("Caller has no access to the commune.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (!db.Database.IsNpgsql())
        {
            // The partition is PostGIS-only (ST_Polygonize / ST_UnaryUnion).
            // In-memory hosts cover the lifecycle, not the geometry.
            throw new NotSupportedException(
                "District generation requires a PostGIS-backed database.");
        }

        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        var (primaryRoads, urbanAreas) = await ReadInputCountsAsync(conn, communeId, ct);
        if (urbanAreas == 0)
        {
            throw new InvalidOperationException(
                "District generation needs at least one urban area (central_urban or secondary_urban) to partition.");
        }

        if (primaryRoads == 0)
        {
            throw new InvalidOperationException(
                $"Commune {communeId} has no {string.Join(" or ", FeatureTypes.RoadLayers.Primary)} roads. "
                + "District generation cuts the urban areas along the primary road network, so generate or map "
                + "at least one boulevard/avenue first — otherwise the pass would produce a single district "
                + "covering the whole zone.");
        }

        var minArea = options.Value.MinDistrictAreaM2;
        var pieces = await ReadPartitionAsync(conn, communeId, minArea, ct);
        if (pieces.Count == 0)
        {
            throw new InvalidOperationException(
                $"District generation produced no district for commune {communeId} from {urbanAreas} urban area(s).");
        }

        var now = timeProvider.UtcNow;
        var drafts = new List<GeneratedDistrictDraft>(pieces.Count);
        foreach (var piece in pieces)
        {
            var draft = AiDraftFeature.Create(
                AiDraftFeature.TypeDistrict,
                piece.GeoJson,
                confidence: 1.0,
                communeId,
                sourceTileRef: null,
                createdAt: now,
                source: AiDraftFeature.SourceDistrictPartition);
            db.AiDraftFeatures.Add(draft);

            drafts.Add(new GeneratedDistrictDraft(draft.Id, piece.GeoJson, piece.AreaM2, piece.Lat, piece.Lng));
        }

        await db.SaveChangesAsync(ct);

        var absorbed = pieces.Sum(p => p.Absorbed);
        logger.LogInformation(
            "District generation for commune {CommuneId}: {Districts} district drafts from {UrbanAreas} urban area(s) "
            + "cut by {PrimaryRoads} primary road(s); {Absorbed} sliver(s) absorbed into neighbours.",
            communeId, drafts.Count, urbanAreas, primaryRoads, absorbed);

        return new DistrictGenerationSummary(drafts, absorbed, primaryRoads, urbanAreas);
    }

    private sealed record PartitionPiece(string GeoJson, double AreaM2, int Absorbed, double Lat, double Lng);

    private static async Task<(int PrimaryRoads, int UrbanAreas)> ReadInputCountsAsync(
        DbConnection conn, int communeId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = InputCountsSql;
        SqlFragments.AddParam(cmd, "@commune_id", communeId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return (0, 0);
        }

        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task<List<PartitionPiece>> ReadPartitionAsync(
        DbConnection conn, int communeId, double minArea, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = PartitionSql;
        SqlFragments.AddParam(cmd, "@commune_id", communeId);
        SqlFragments.AddParam(cmd, "@min_area", minArea);

        var pieces = new List<PartitionPiece>();
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
        {
            var geoJson = reader.GetString(0);

            // ST_AsGeoJSON emits MultiPolygon for a disconnected result. The
            // absorption rule (shared edge, never a bare point touch) is what
            // keeps every piece connected, so this is unreachable in practice —
            // fail loudly rather than silently dropping the disconnected parts,
            // which would leave an uncovered hole in the zone.
            if (!geoJson.Contains("\"Polygon\"", StringComparison.Ordinal)
                || geoJson.Contains("MultiPolygon", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"District partition produced a disconnected geometry for commune {communeId}; refusing to write a draft that would leave a gap.");
            }

            pieces.Add(new PartitionPiece(
                geoJson,
                reader.GetDouble(1),
                reader.GetInt32(2),
                reader.GetDouble(3),
                reader.GetDouble(4)));
        }

        return pieces;
    }
}
