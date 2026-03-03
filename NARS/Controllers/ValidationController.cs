using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Tags("Validation")]
public class ValidationController(AppDbContext db, JwtService jwt) : ControllerBase
{
    // ── SQL helper: reconstruct a POLYGON geometry from features.data ─────────
    // features.data stores coordinates as JSON: {"coordinates": [{"lat":..,"lng":..}, ...]}
    // PostGIS GeoJSON expects [lng, lat] ordering.
    private const string PolygonFromDataSql = @"
        ST_SetSRID(ST_GeomFromGeoJSON(
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
        ), 4326)";

    // ── SQL helper: reconstruct a LINESTRING geometry from features.data ──────
    private const string LineStringFromDataSql = @"
        ST_SetSRID(ST_GeomFromGeoJSON(
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
        ), 4326)";

    // ── GET /api/validate/area/main-urban-exists ──────────────────────────────
    // Returns whether the user already has a central_urban area.
    // Rule: only ONE main urban area is allowed per municipality.

    [HttpGet("/api/validate/area/main-urban-exists")]
    public async Task<IActionResult> MainUrbanExists()
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var exists = await db.Features.AnyAsync(f =>
            f.UserId == user.UserId &&
            f.Type   == FeatureTypes.Area &&
            f.Layer  == FeatureTypes.AreaLayers.CentralUrban);

        return Ok(new { exists });
    }

    // ── POST /api/validate/road ───────────────────────────────────────────────
    // Validates a new road polyline against two hard rules:
    //   1. No segment turn > 90°  (computed in C#, no PostGIS needed)
    //   2. Must connect to at least one existing road within 20 m
    //      (first road in the municipality is exempt)

    [HttpPost("/api/validate/road")]
    public async Task<IActionResult> ValidateRoad([FromBody] ValidateRoadRequest body)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        if (body.Coordinates.Count < 2)
            return BadRequest(new ValidateRoadResponse(false, "A road must have at least 2 points."));

        // ── Rule 1: angle check (pure C#) ─────────────────────────────────────
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

        // ── Rule 2: connectivity check (PostGIS) ───────────────────────────────
        var existingCount = await db.Features.CountAsync(f =>
            f.UserId == user.UserId && f.Type == FeatureTypes.Road);

        if (existingCount == 0)
            return Ok(new ValidateRoadResponse(true, null));  // first road: exempt

        // Build WKT for the new road (PostGIS uses X=lng, Y=lat)
        var wkt = BuildLineStringWkt(body.Coordinates);

        var conn = db.Database.GetDbConnection();
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
                            ({LineStringFromDataSql})::geography,
                            ST_SetSRID(ST_GeomFromText(@wkt), 4326)::geography,
                            20
                          )
                )";
            AddParam(cmd, "@uid", user.UserId);
            AddParam(cmd, "@wkt", wkt);

            var result = await cmd.ExecuteScalarAsync();
            bool connected = Convert.ToBoolean(result);

            if (!connected)
                return Ok(new ValidateRoadResponse(false,
                    "This road must connect to at least one existing road (within 20 m of an endpoint)."));
        }
        finally { await conn.CloseAsync(); }

        return Ok(new ValidateRoadResponse(true, null));
    }

    // ── POST /api/validate/district ───────────────────────────────────────────
    // Validates a new district polygon:
    //   1. Must touch (share a boundary with) at least one existing district
    //      — first district is exempt
    //   2. Must not overlap any existing district

    [HttpPost("/api/validate/district")]
    public async Task<IActionResult> ValidateDistrict([FromBody] ValidateDistrictRequest body)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        if (body.Coordinates.Count < 3)
            return BadRequest(new ValidateDistrictResponse(false, "A district must have at least 3 points."));

        var existingCount = await db.Features.CountAsync(f =>
            f.UserId == user.UserId && f.Type == FeatureTypes.District);

        if (existingCount == 0)
            return Ok(new ValidateDistrictResponse(true, null));  // first district: exempt

        var wkt = BuildPolygonWkt(body.Coordinates);

        var conn = db.Database.GetDbConnection();
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
                                ({PolygonFromDataSql}),
                                ST_SetSRID(ST_GeomFromText(@wkt), 4326)
                              )
                    )";
                AddParam(cmd, "@uid", user.UserId);
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
                                ({PolygonFromDataSql})
                            )
                            OR ST_Intersects(
                                ST_Boundary(ST_SetSRID(ST_GeomFromText(@wkt), 4326)),
                                ST_Boundary({PolygonFromDataSql})
                            )
                          )
                    )";
                AddParam(cmd, "@uid", user.UserId);
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
    // Checks whether all districts together fully cover all urban areas.
    // Called as a phase gate before allowing the user to advance from Districts → Roads.

    [HttpGet("/api/validate/districts/coverage")]
    public async Task<IActionResult> DistrictsCoverage()
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var urbanCount = await db.Features.CountAsync(f =>
            f.UserId == user.UserId &&
            f.Type   == FeatureTypes.Area &&
            (f.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             f.Layer == FeatureTypes.AreaLayers.SecondaryUrban));

        if (urbanCount == 0)
            return Ok(new DistrictCoverageResponse(true, "No urban areas to cover."));

        var districtCount = await db.Features.CountAsync(f =>
            f.UserId == user.UserId && f.Type == FeatureTypes.District);

        if (districtCount == 0)
            return Ok(new DistrictCoverageResponse(false,
                "No districts have been drawn yet. Districts must fully cover all urban areas."));

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        bool covered;
        try
        {
            using var cmd = conn.CreateCommand();
            // Use a small buffer (≈11 m) to tolerate floating-point boundary imprecision
            cmd.CommandText = $@"
                WITH
                urban AS (
                    SELECT ST_Union({PolygonFromDataSql}) AS geom
                    FROM features f
                    WHERE f.user_id = @uid
                      AND f.type   = 'area'
                      AND f.layer  IN ('central_urban', 'secondary_urban')
                ),
                districts AS (
                    SELECT ST_Union({PolygonFromDataSql}) AS geom
                    FROM features f
                    WHERE f.user_id = @uid
                      AND f.type   = 'district'
                )
                SELECT ST_Covers(
                    ST_Buffer(districts.geom::geography, 10)::geometry,
                    urban.geom
                )
                FROM urban, districts
                WHERE urban.geom IS NOT NULL AND districts.geom IS NOT NULL";
            AddParam(cmd, "@uid", user.UserId);

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

    // ── POST /api/road-side ───────────────────────────────────────────────────
    // Determines whether a marker (house entrance) falls on the left or right
    // side of a given road using the cross-product of the nearest road segment.
    // Also returns the next suggested entrance number:
    //   Left side  → next odd  number not already used on this road
    //   Right side → next even number not already used on this road

    [HttpPost("/api/road-side")]
    public async Task<IActionResult> GetRoadSide([FromBody] RoadSideRequest body)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var road = await db.Features.FirstOrDefaultAsync(f =>
            f.Id == body.RoadId && f.UserId == user.UserId && f.Type == FeatureTypes.Road);

        if (road is null)
            return NotFound(new { detail = "Road not found." });

        // Parse road coordinates from stored JSON
        var roadData = JsonSerializer.Deserialize<JsonElement>(road.Data);
        var coordsEl = roadData.GetProperty("coordinates");
        var roadCoords = coordsEl.EnumerateArray()
            .Select(c => (Lat: c.GetProperty("lat").GetDouble(),
                          Lng: c.GetProperty("lng").GetDouble()))
            .ToList();

        if (roadCoords.Count < 2)
            return BadRequest(new { detail = "Road has insufficient coordinates." });

        // Find the nearest segment to the marker
        double markerLat = body.Lat, markerLng = body.Lng;
        double minDist = double.MaxValue;
        int nearestIdx = 0;

        for (int i = 0; i < roadCoords.Count - 1; i++)
        {
            var mid = ((roadCoords[i].Lat + roadCoords[i + 1].Lat) / 2,
                       (roadCoords[i].Lng + roadCoords[i + 1].Lng) / 2);
            double d = Math.Sqrt(Math.Pow(markerLat - mid.Item1, 2) +
                                 Math.Pow(markerLng - mid.Item2, 2));
            if (d < minDist) { minDist = d; nearestIdx = i; }
        }

        var p1 = roadCoords[nearestIdx];
        var p2 = roadCoords[nearestIdx + 1];

        // Cross product: positive → left, negative → right
        // (p2 - p1) × (marker - p1)
        double cross = (p2.Lng - p1.Lng) * (markerLat - p1.Lat)
                     - (p2.Lat - p1.Lat) * (markerLng - p1.Lng);

        string side = cross >= 0 ? "left" : "right";

        // Collect already-used entrance numbers on this road
        var existingEntrances = await db.Features
            .Where(f => f.UserId == user.UserId && f.Type == FeatureTypes.HouseEntrance
                     && f.Layer == FeatureTypes.HouseEntranceLayers.Main)
            .ToListAsync();

        var usedNumbers = new HashSet<int>();
        foreach (var e in existingEntrances)
        {
            try
            {
                var d = JsonSerializer.Deserialize<JsonElement>(e.Data);
                if (d.TryGetProperty("roadDbId", out var rdId) && rdId.GetInt32() == body.RoadId
                    && d.TryGetProperty("entranceNumber", out var num))
                    usedNumbers.Add(num.GetInt32());
            }
            catch { /* skip malformed entries */ }
        }

        // Next available odd (left) or even (right)
        int suggested = side == "left" ? 1 : 2;
        int step      = 2;
        while (usedNumbers.Contains(suggested))
            suggested += step;

        return Ok(new RoadSideResponse(side, suggested));
    }

    // ── POST /api/areas/refresh-scattered ────────────────────────────────────
    // Recomputes the scattered area as:
    //   municipal_boundary  MINUS  ST_Union(all central_urban + secondary_urban areas)
    //
    // Deletes all existing scattered area records for the user and replaces them
    // with the freshly computed geometry stored as GeoJSON in the data field.
    // Called automatically after every area save/edit/delete.

    [HttpPost("/api/areas/refresh-scattered")]
    public async Task<IActionResult> RefreshScattered()
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        // Resolve commune ID from JWT
        var token     = Request.Cookies["access_token"]!;
        var principal = jwt.ValidateToken(token)!;
        if (!int.TryParse(principal.FindFirst("commune_id")?.Value, out int communeId))
            return BadRequest(new { detail = "commune_id claim missing from token." });

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        string? scatteredGeoJson = null;
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                WITH
                boundary AS (
                    SELECT geometry AS geom
                    FROM communes_boundaries
                    WHERE commune_id = @cid
                ),
                urban AS (
                    SELECT ST_Union({PolygonFromDataSql}) AS geom
                    FROM features f
                    WHERE f.user_id = @uid
                      AND f.type   = 'area'
                      AND f.layer  IN ('central_urban', 'secondary_urban')
                )
                SELECT ST_AsGeoJSON(
                    ST_Difference(
                        boundary.geom,
                        COALESCE(urban.geom, ST_GeomFromText('GEOMETRYCOLLECTION EMPTY', 4326))
                    )
                )
                FROM boundary
                LEFT JOIN urban ON true";
            AddParam(cmd, "@cid", communeId);
            AddParam(cmd, "@uid", user.UserId);

            scatteredGeoJson = await cmd.ExecuteScalarAsync() as string;
        }
        finally { await conn.CloseAsync(); }

        if (scatteredGeoJson is null)
            return Ok(new ScatteredRefreshResponse(false, null, "Municipal boundary not found."));

        // Delete previous scattered records and insert a fresh one
        await db.Features
            .Where(f => f.UserId == user.UserId &&
                        f.Type   == FeatureTypes.Area &&
                        f.Layer  == FeatureTypes.AreaLayers.Scattered)
            .ExecuteDeleteAsync();

        var scattered = new Feature
        {
            UserId = user.UserId,
            Type   = FeatureTypes.Area,
            Layer  = FeatureTypes.AreaLayers.Scattered,
            Label  = "Scattered Area",
            Data   = JsonSerializer.Serialize(new
            {
                type     = "areas",
                label    = "Scattered Area",
                layer    = "scattered",
                geometry = scatteredGeoJson,
            }),
        };
        db.Features.Add(scattered);
        await db.SaveChangesAsync();

        return Ok(new ScatteredRefreshResponse(true, scatteredGeoJson, "Scattered area recomputed."));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (int UserId, string Username)? RequireAuth()
    {
        var token = Request.Cookies["access_token"];
        if (token is null) return null;
        var principal = jwt.ValidateToken(token);
        if (principal is null) return null;
        var userId   = principal.FindFirst("user_id")?.Value;
        var username = principal.FindFirst("username")?.Value;
        if (userId is null) return null;
        return (int.Parse(userId), username ?? string.Empty);
    }

    /// <summary>Builds a WKT LINESTRING from coordinate list (lng lat order for PostGIS).</summary>
    private static string BuildLineStringWkt(List<CoordDto> coords) =>
        $"LINESTRING({string.Join(",", coords.Select(c => $"{c.Lng} {c.Lat}"))})";

    /// <summary>Builds a WKT POLYGON from coordinate list (closes the ring automatically).</summary>
    private static string BuildPolygonWkt(List<CoordDto> coords)
    {
        var pts = coords.Select(c => $"{c.Lng} {c.Lat}").ToList();
        if (pts[0] != pts[^1]) pts.Add(pts[0]);  // close ring
        return $"POLYGON(({string.Join(",", pts)}))";
    }

    private static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value         = value;
        cmd.Parameters.Add(p);
    }
}
