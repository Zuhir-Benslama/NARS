using System.Globalization;
using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;


namespace NarsApi.Controllers;

// fix #2 & #9: Extends NarsControllerBase ([Authorize] + CurrentUserId/CurrentCommuneId)
// instead of duplicating the manual RequireAuth() helper.
[ApiController]
[Route("/api")]
[Tags("Validation")]
public class ValidationController(
    AppDbContext db,
    IConfiguration config) : NarsControllerBase
{
    private double DistrictBoundaryToleranceMeters =>
        double.TryParse(config["Validation:DistrictBoundaryToleranceMeters"], out var v) ? v : 10.0;

    private double MaxRoadTurnAngleDegrees =>
        double.TryParse(config["Validation:RoadTurnAngleDegrees"], out var v) ? v : 90.0;

    private double RoadConnectivityDistanceMeters =>
        double.TryParse(config["Validation:RoadConnectivityMeters"], out var v) ? v : 20.0;

    private int MaxCoordinateCount =>
        int.TryParse(config["Validation:MaxCoordinateCount"], out var v) ? v : 10_000;

    // ── Feature table names (from registry — single source of truth) ─────────
    private static string RoadTable => FeatureTypeRegistry.GetDescriptor(FeatureTypes.Road)?.TableName ?? "roads";
    private static string AreaTable => FeatureTypeRegistry.GetDescriptor(FeatureTypes.Area)?.TableName ?? "areas";
    private static string DistrictTable => FeatureTypeRegistry.GetDescriptor(FeatureTypes.District)?.TableName ?? "districts";

    // fix #4: PolygonFromDataSql and LineStringFromDataSql are now imported from
    // SqlFragments (both include ST_MakeValid) instead of being declared locally.
    // Use the shared constants directly in all queries below.

    // ── GET /api/validate/area/main-urban-exists ──────────────────────────────

    [HttpGet("validate/area/main-urban-exists")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MainUrbanExists(CancellationToken cancellationToken = default)
    {
        var exists = await db.Areas.AnyAsync(f =>
            f.UserId == CurrentUserId &&
            f.Layer == FeatureTypes.AreaLayers.CentralUrban, cancellationToken);

        return Ok(new { exists });
    }

    // ── POST /api/validate/road ───────────────────────────────────────────────

    [HttpPost("validate/road")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateRoad([FromBody] ValidateRoadRequest body, CancellationToken cancellationToken = default)
    {
        if (body.Coordinates.Count < 2)
            return BadRequest(new ValidateRoadResponse(false, "A road must have at least 2 points."));

        // Bounds checking: prevent DoS via excessive coordinate counts.
        if (!CheckCoordinateBounds(body.Coordinates, out var boundsError))
            return BadRequest(new ValidateRoadResponse(false, boundsError!));

        // Rule 1: angle check (pure C#)
        for (int i = 0; i < body.Coordinates.Count - 2; i++)
        {
            var A = body.Coordinates[i];
            var B = body.Coordinates[i + 1];
            var C = body.Coordinates[i + 2];

            double v1x = B.Lng - A.Lng, v1y = B.Lat - A.Lat;
            double v2x = C.Lng - B.Lng, v2y = C.Lat - B.Lat;

            double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            if (len1 < 1e-10 || len2 < 1e-10) continue;

            double dot = (v1x * v2x + v1y * v2y) / (len1 * len2);
            double angle = Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * (180.0 / Math.PI);

            if (angle > MaxRoadTurnAngleDegrees)
                return Ok(new ValidateRoadResponse(false,
                    $"Road turn at point {i + 2} is {angle:F1}°, which exceeds the {MaxRoadTurnAngleDegrees}° maximum."));
        }

        // Rule 2: connectivity check (PostGIS) — first road is exempt
        var existingCount = await db.Roads.CountAsync(f => f.UserId == CurrentUserId, cancellationToken);

        if (existingCount == 0)
            return Ok(new ValidateRoadResponse(true, null));

        var wkt = BuildLineStringWkt(body.Coordinates);
        var conn = db.Database.GetDbConnection();

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT EXISTS (
                    SELECT 1
                    FROM {RoadTable} f
                    WHERE f.user_id = @uid
                      AND ST_DWithin(
                            ({SqlFragments.LineStringFromData})::geography,
                            ST_SetSRID(ST_GeomFromText(@wkt), 4326)::geography,
                            {RoadConnectivityDistanceMeters}
                          )
                )";
            AddParam(cmd, "@uid", RequiredCurrentUserId);
            AddParam(cmd, "@wkt", wkt);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            bool connected = Convert.ToBoolean(result);

            if (!connected)
                return Ok(new ValidateRoadResponse(false,
                    "This road must connect to at least one existing road (within 20 m of an endpoint)."));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return Ok(new ValidateRoadResponse(true, null));
    }

    // ── POST /api/validate/district ───────────────────────────────────────────

    [HttpPost("validate/district")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateDistrict([FromBody] ValidateDistrictRequest body, CancellationToken cancellationToken = default)
    {
        if (body.Coordinates.Count < 3)
            return BadRequest(new ValidateDistrictResponse(false, "A district must have at least 3 points."));
        if (!CheckCoordinateBounds(body.Coordinates, out var inputError))
            return BadRequest(new ValidateDistrictResponse(false, inputError!));

        var existingCount = await db.Districts.CountAsync(f => f.UserId == CurrentUserId, cancellationToken);

        if (existingCount == 0)
            return Ok(new ValidateDistrictResponse(true, null));

        var wkt = BuildPolygonWkt(body.Coordinates);
        var conn = db.Database.GetDbConnection();

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (await CheckOverlapAsync(conn, wkt, cancellationToken))
                return Ok(new ValidateDistrictResponse(false,
                    "This district overlaps an existing district. Districts must share edges but not overlap."));

            var skipAdjacency = body.DistrictTypeKey == FeatureTypes.DistrictLayers.TradActivitiesZone ||
                                body.DistrictTypeKey == FeatureTypes.DistrictLayers.IndustryZone;

            if (!skipAdjacency)
            {
                var siblings = await CountSiblingsInSameAreaAsync(conn, wkt, cancellationToken);
                if (siblings > 0 && !await CheckAdjacencyAsync(conn, wkt, cancellationToken))
                    return Ok(new ValidateDistrictResponse(false,
                        "This district does not connect to any existing district in this urban area. Districts must share a boundary (no gaps)."));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return Ok(new ValidateDistrictResponse(true, null));
    }

    // ── GET /api/validate/districts/coverage ─────────────────────────────────

    [HttpGet("validate/districts/coverage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DistrictsCoverage(CancellationToken cancellationToken = default)
    {
        var urbanCount = await db.Areas.CountAsync(f =>
            f.UserId == CurrentUserId &&
            (f.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             f.Layer == FeatureTypes.AreaLayers.SecondaryUrban), cancellationToken);

        if (urbanCount == 0)
            return Ok(new DistrictCoverageResponse(true, "No urban areas to cover."));

        var districtCount = await db.Districts.CountAsync(f => f.UserId == CurrentUserId, cancellationToken);

        if (districtCount == 0)
            return Ok(new DistrictCoverageResponse(false,
                "No districts have been drawn yet. Districts must fully cover all urban areas."));

        var conn = db.Database.GetDbConnection();
        bool covered;
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            using var cmd = conn.CreateCommand();
            // fix #13: DistrictBoundaryToleranceMeters replaces the magic literal 10.
            cmd.CommandText = $@"
                WITH
                urban AS (
                    SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                    FROM {AreaTable} f
                    WHERE f.user_id = @uid
                      AND f.layer  IN ('central_urban', 'secondary_urban')
                ),
                districts AS (
                    SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                    FROM {DistrictTable} f
                    WHERE f.user_id = @uid
                )
                SELECT ST_Covers(
                    ST_Buffer(districts.geom::geography, {DistrictBoundaryToleranceMeters})::geometry,
                    urban.geom
                )
                FROM urban, districts
                WHERE urban.geom IS NOT NULL AND districts.geom IS NOT NULL";
            AddParam(cmd, "@uid", RequiredCurrentUserId);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            covered = result is bool b && b;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return Ok(new DistrictCoverageResponse(
            covered,
            covered
                ? "All urban areas are fully covered by districts."
                : "Districts do not yet fully cover all urban areas. Please fill any remaining gaps before proceeding."));
    }

    // ── Input validation ───────────────────────────────────────

    private bool CheckCoordinateBounds(List<CoordDto> coords, out string? error)
    {
        if (coords.Count > MaxCoordinateCount) { error = $"Too many coordinates (max {MaxCoordinateCount:N0})."; return false; }
        if (coords.Any(c => double.IsNaN(c.Lat) || double.IsInfinity(c.Lat) ||
                            double.IsNaN(c.Lng) || double.IsInfinity(c.Lng)))
        {
            error = "Invalid coordinate values (NaN or Infinity).";
            return false;
        }
        error = null;
        return true;
    }

    // ── Query helpers ─────────────────────────────────────────

    private async Task<bool> CheckOverlapAsync(DbConnection conn, string wkt, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
            SELECT EXISTS (
                SELECT 1 FROM {DistrictTable} f
                WHERE f.user_id = @uid
                  AND ST_Intersects(
                        ({SqlFragments.PolygonFromData}),
                        ST_SetSRID(ST_GeomFromText(@wkt), 4326)
                      )
            )";
        AddParam(cmd, "@uid", RequiredCurrentUserId);
        AddParam(cmd, "@wkt", wkt);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is bool b && b;
    }

    private async Task<long> CountSiblingsInSameAreaAsync(DbConnection conn, string wkt, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
            SELECT COUNT(*) FROM {DistrictTable} d
            WHERE d.user_id = @uid
              AND EXISTS (
                  SELECT 1 FROM {AreaTable} a
                  WHERE a.user_id = @uid
                    AND a.layer IN ('central_urban', 'secondary_urban')
                    AND ST_Intersects(({SqlFragments.PolygonFromDataWithAlias("a")}), ST_SetSRID(ST_GeomFromText(@wkt), 4326))
                    AND ST_Intersects(({SqlFragments.PolygonFromDataWithAlias("a")}), ({SqlFragments.PolygonFromDataWithAlias("d")}))
              )";
        AddParam(cmd, "@uid", RequiredCurrentUserId);
        AddParam(cmd, "@wkt", wkt);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> CheckAdjacencyAsync(DbConnection conn, string wkt, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
            SELECT EXISTS (
                SELECT 1 FROM {DistrictTable} f
                WHERE f.user_id = @uid
                  AND (ST_Touches(ST_SetSRID(ST_GeomFromText(@wkt), 4326), ({SqlFragments.PolygonFromData}))
                       OR ST_Intersects(ST_Boundary(ST_SetSRID(ST_GeomFromText(@wkt), 4326)), ST_Boundary({SqlFragments.PolygonFromData})))
                  AND EXISTS (
                      SELECT 1 FROM {AreaTable} a
                      WHERE a.user_id = @uid
                        AND a.layer IN ('central_urban', 'secondary_urban')
                        AND ST_Intersects(({SqlFragments.PolygonFromDataWithAlias("a")}), ST_SetSRID(ST_GeomFromText(@wkt), 4326))
                        AND ST_Intersects(({SqlFragments.PolygonFromDataWithAlias("a")}), ({SqlFragments.PolygonFromDataWithAlias("f")}))
                  )
            )";
        AddParam(cmd, "@uid", RequiredCurrentUserId);
        AddParam(cmd, "@wkt", wkt);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is bool b && b;
    }

    // ── WKT builders ───────────────────────────────────────────

    // Use InvariantCulture so doubles always format with '.' decimal separator,
    // regardless of server locale (e.g. fr-DZ would use ',' otherwise).
    private static string FormatDouble(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static string BuildLineStringWkt(List<CoordDto> coords) =>
        $"LINESTRING({string.Join(",", coords.Select(c => $"{FormatDouble(c.Lng)} {FormatDouble(c.Lat)}"))})";

    private static string BuildPolygonWkt(List<CoordDto> coords)
    {
        var pts = coords.Select(c => $"{FormatDouble(c.Lng)} {FormatDouble(c.Lat)}").ToList();
        if (pts[0] != pts[^1]) pts.Add(pts[0]);
        return $"POLYGON(({string.Join(",", pts)}))";
    }
}
