using System.Data;
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
[Tags("Validation")]
public class ValidationController(AppDbContext db) : NarsControllerBase
{
    // fix #13: named constant replaces the magic number tolerance buffer.
    private const double DistrictBoundaryToleranceMeters = 10.0;

    // fix #4: PolygonFromDataSql and LineStringFromDataSql are now imported from
    // SqlFragments (both include ST_MakeValid) instead of being declared locally.
    // Use the shared constants directly in all queries below.

    // ── GET /api/validate/area/main-urban-exists ──────────────────────────────

    [HttpGet("/api/validate/area/main-urban-exists")]
    public async Task<IActionResult> MainUrbanExists()
    {
        var exists = await db.Features.AnyAsync(f =>
            f.UserId == CurrentUserId &&
            f.Type   == FeatureTypes.Area &&
            f.Layer  == FeatureTypes.AreaLayers.CentralUrban);

        return Ok(new { exists });
    }

    // ── POST /api/validate/road ───────────────────────────────────────────────

    [HttpPost("/api/validate/road")]
    public async Task<IActionResult> ValidateRoad([FromBody] ValidateRoadRequest body)
    {
        if (body.Coordinates.Count < 2)
            return BadRequest(new ValidateRoadResponse(false, "A road must have at least 2 points."));

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

            double dot   = (v1x * v2x + v1y * v2y) / (len1 * len2);
            double angle = Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * (180.0 / Math.PI);

            if (angle > 90.0)
                return Ok(new ValidateRoadResponse(false,
                    $"Road turn at point {i + 2} is {angle:F1}°, which exceeds the 90° maximum."));
        }

        // Rule 2: connectivity check (PostGIS) — first road is exempt
        var existingCount = await db.Features.CountAsync(f =>
            f.UserId == CurrentUserId && f.Type == FeatureTypes.Road);

        if (existingCount == 0)
            return Ok(new ValidateRoadResponse(true, null));

        var wkt  = BuildLineStringWkt(body.Coordinates);
        var conn = db.Database.GetDbConnection();

        // fix #10: guard against already-open pooled connection
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT EXISTS (
                    SELECT 1
                    FROM features f
                    WHERE f.user_id = @uid
                      AND f.type = 'road'
                      AND ST_DWithin(
                            ({SqlFragments.LineStringFromData})::geography,
                            ST_SetSRID(ST_GeomFromText(@wkt), 4326)::geography,
                            20
                          )
                )";
            AddParam(cmd, "@uid", CurrentUserId);
            AddParam(cmd, "@wkt", wkt);

            var result    = await cmd.ExecuteScalarAsync();
            bool connected = Convert.ToBoolean(result);

            if (!connected)
                return Ok(new ValidateRoadResponse(false,
                    "This road must connect to at least one existing road (within 20 m of an endpoint)."));
        }
        finally { await conn.CloseAsync(); }

        return Ok(new ValidateRoadResponse(true, null));
    }

    // ── POST /api/validate/district ───────────────────────────────────────────

    [HttpPost("/api/validate/district")]
    public async Task<IActionResult> ValidateDistrict([FromBody] ValidateDistrictRequest body)
    {
        if (body.Coordinates.Count < 3)
            return BadRequest(new ValidateDistrictResponse(false, "A district must have at least 3 points."));

        var existingCount = await db.Features.CountAsync(f =>
            f.UserId == CurrentUserId && f.Type == FeatureTypes.District);

        if (existingCount == 0)
            return Ok(new ValidateDistrictResponse(true, null));

        var wkt  = BuildPolygonWkt(body.Coordinates);
        var conn = db.Database.GetDbConnection();

        // fix #10: guard against already-open pooled connection
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();
        try
        {
            // Check overlap — hard block
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT EXISTS (
                        SELECT 1
                        FROM features f
                        WHERE f.user_id = @uid
                          AND f.type = 'district'
                          AND ST_Overlaps(
                                ({SqlFragments.PolygonFromData}),
                                ST_SetSRID(ST_GeomFromText(@wkt), 4326)
                              )
                    )";
                AddParam(cmd, "@uid", CurrentUserId);
                AddParam(cmd, "@wkt", wkt);

                var overlaps = Convert.ToBoolean(await cmd.ExecuteScalarAsync());
                if (overlaps)
                    return Ok(new ValidateDistrictResponse(false,
                        "This district overlaps an existing district. Districts must share edges but not overlap."));
            }

            // Check adjacency — must touch at least one existing district
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT EXISTS (
                        SELECT 1
                        FROM features f
                        WHERE f.user_id = @uid
                          AND f.type = 'district'
                          AND (
                            ST_Touches(
                                ST_SetSRID(ST_GeomFromText(@wkt), 4326),
                                ({SqlFragments.PolygonFromData})
                            )
                            OR ST_Intersects(
                                ST_Boundary(ST_SetSRID(ST_GeomFromText(@wkt), 4326)),
                                ST_Boundary({SqlFragments.PolygonFromData})
                            )
                          )
                    )";
                AddParam(cmd, "@uid", CurrentUserId);
                AddParam(cmd, "@wkt", wkt);

                var touches = Convert.ToBoolean(await cmd.ExecuteScalarAsync());
                if (!touches)
                    return Ok(new ValidateDistrictResponse(false,
                        "This district does not connect to any existing district. Districts must share a boundary (no gaps)."));
            }
        }
        finally { await conn.CloseAsync(); }

        return Ok(new ValidateDistrictResponse(true, null));
    }

    // ── GET /api/validate/districts/coverage ─────────────────────────────────

    [HttpGet("/api/validate/districts/coverage")]
    public async Task<IActionResult> DistrictsCoverage()
    {
        var urbanCount = await db.Features.CountAsync(f =>
            f.UserId == CurrentUserId &&
            f.Type   == FeatureTypes.Area &&
            (f.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             f.Layer == FeatureTypes.AreaLayers.SecondaryUrban));

        if (urbanCount == 0)
            return Ok(new DistrictCoverageResponse(true, "No urban areas to cover."));

        var districtCount = await db.Features.CountAsync(f =>
            f.UserId == CurrentUserId && f.Type == FeatureTypes.District);

        if (districtCount == 0)
            return Ok(new DistrictCoverageResponse(false,
                "No districts have been drawn yet. Districts must fully cover all urban areas."));

        var conn = db.Database.GetDbConnection();

        // fix #10: guard against already-open pooled connection
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();
        bool covered;
        try
        {
            using var cmd = conn.CreateCommand();
            // fix #13: DistrictBoundaryToleranceMeters replaces the magic literal 10.
            cmd.CommandText = $@"
                WITH
                urban AS (
                    SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                    FROM features f
                    WHERE f.user_id = @uid
                      AND f.type   = 'area'
                      AND f.layer  IN ('central_urban', 'secondary_urban')
                ),
                districts AS (
                    SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                    FROM features f
                    WHERE f.user_id = @uid
                      AND f.type   = 'district'
                )
                SELECT ST_Covers(
                    ST_Buffer(districts.geom::geography, {DistrictBoundaryToleranceMeters})::geometry,
                    urban.geom
                )
                FROM urban, districts
                WHERE urban.geom IS NOT NULL AND districts.geom IS NOT NULL";
            AddParam(cmd, "@uid", CurrentUserId);

            var result = await cmd.ExecuteScalarAsync();
            covered = result is not null && Convert.ToBoolean(result);
        }
        finally { await conn.CloseAsync(); }

        return Ok(new DistrictCoverageResponse(
            covered,
            covered
                ? "All urban areas are fully covered by districts."
                : "Districts do not yet fully cover all urban areas. Please fill any remaining gaps before proceeding."));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildLineStringWkt(List<CoordDto> coords) =>
        $"LINESTRING({string.Join(",", coords.Select(c => $"{c.Lng} {c.Lat}"))})";

    private static string BuildPolygonWkt(List<CoordDto> coords)
    {
        var pts = coords.Select(c => $"{c.Lng} {c.Lat}").ToList();
        if (pts[0] != pts[^1]) pts.Add(pts[0]);
        return $"POLYGON(({string.Join(",", pts)}))";
    }
}
