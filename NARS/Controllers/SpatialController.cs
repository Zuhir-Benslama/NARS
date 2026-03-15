using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Controllers;

/// <summary>
/// Spatial query endpoints: road-side determination and scattered-area recomputation.
/// These are operational/GIS queries distinct from the shape-validation rules in
/// <see cref="ValidationController"/>.
/// </summary>
[ApiController]
[Tags("Spatial")]
public class SpatialController(AppDbContext db) : NarsControllerBase
{
    // ── POST /api/road-side ───────────────────────────────────────────────────

    [HttpPost("/api/road-side")]
    public async Task<IActionResult> GetRoadSide([FromBody] RoadSideRequest body)
    {
        var road = await db.Roads.FirstOrDefaultAsync(f =>
            f.Id == body.RoadId && f.UserId == CurrentUserId);

        if (road is null)
            return NotFound(new { detail = "Road not found." });

        var roadData   = JsonSerializer.Deserialize<JsonElement>(road.Data);
        var coordsEl   = roadData.GetProperty("coordinates");
        var roadCoords = coordsEl.EnumerateArray()
            .Select(c => (Lat: c.GetProperty("lat").GetDouble(),
                          Lng: c.GetProperty("lng").GetDouble()))
            .ToList();

        if (roadCoords.Count < 2)
            return BadRequest(new { detail = "Road has insufficient coordinates." });

        // Find the nearest segment midpoint to the marker
        double markerLat = body.Lat, markerLng = body.Lng;
        double minDist   = double.MaxValue;
        int    nearestIdx = 0;

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
        double cross = (p2.Lng - p1.Lng) * (markerLat - p1.Lat)
                     - (p2.Lat - p1.Lat) * (markerLng - p1.Lng);

        string side = cross >= 0 ? "left" : "right";

        // fix #8: filter entrances by roadDbId inside PostgreSQL via JSONB operators
        // instead of loading all entrances into memory and filtering in C#.
        var usedNumbers = new HashSet<int>();
        var conn        = db.Database.GetDbConnection();

        // fix #10: guard against already-open pooled connection
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT (data::jsonb->>'entranceNumber')::int
                FROM house_entrances
                WHERE user_id = @uid
                  AND layer   = 'main_entrance'
                  AND road_id = @rid
                  AND data::jsonb->>'entranceNumber' IS NOT NULL";
            AddParam(cmd, "@uid", CurrentUserId);
            AddParam(cmd, "@rid", body.RoadId);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (!reader.IsDBNull(0))
                    usedNumbers.Add(reader.GetInt32(0));
        }
        finally { await conn.CloseAsync(); }

        // Next available odd (left) or even (right) number
        int suggested = side == "left" ? 1 : 2;
        while (usedNumbers.Contains(suggested))
            suggested += 2;

        return Ok(new RoadSideResponse(side, suggested));
    }

    // ── POST /api/areas/refresh-scattered ────────────────────────────────────
    // fix #3: replaced null-forgiving ! operators with proper null guards.
    // fix #2: replaced manual cookie→JWT dance with CurrentCommuneId from [Authorize].

    [HttpPost("/api/areas/refresh-scattered")]
    public async Task<IActionResult> RefreshScattered()
    {
        int communeId = CurrentCommuneId;   // fix #3: no NRE — claim is guaranteed by [Authorize]

        var conn = db.Database.GetDbConnection();

        // fix #10: guard against already-open pooled connection
        if (conn.State != ConnectionState.Open)
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
                    SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                    FROM areas f
                    WHERE f.user_id = @uid
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
            AddParam(cmd, "@uid", CurrentUserId);

            scatteredGeoJson = await cmd.ExecuteScalarAsync() as string;
        }
        finally { await conn.CloseAsync(); }

        if (scatteredGeoJson is null)
            return Ok(new ScatteredRefreshResponse(false, null, "Municipal boundary not found."));

        await db.Areas
            .Where(f => f.UserId == CurrentUserId &&
                        f.Layer  == FeatureTypes.AreaLayers.Scattered)
            .ExecuteDeleteAsync();

        var scattered = new Area
        {
            UserId = CurrentUserId,
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
        db.Areas.Add(scattered);
        await db.SaveChangesAsync();

        return Ok(new ScatteredRefreshResponse(true, scatteredGeoJson, "Scattered area recomputed."));
    }
}
