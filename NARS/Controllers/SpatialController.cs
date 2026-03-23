using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Spatial query endpoints: road-side determination and scattered-area recomputation.
/// These are operational/GIS queries distinct from the shape-validation rules in
/// <see cref="ValidationController"/>.
/// </summary>
[ApiController]
[Tags("Spatial")]
public class SpatialController(AppDbContext db, IScatteredAreaService scatteredService) : NarsControllerBase
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

        // Find the nearest segment midpoint to the marker.
        // Apply cosine correction so the Δlng component is in the same
        // unit scale as Δlat (important at Algeria's latitudes ~28–37°N).
        double markerLat = body.Lat, markerLng = body.Lng;
        double cosLat     = Math.Cos(markerLat * Math.PI / 180.0);
        double minDist    = double.MaxValue;
        int    nearestIdx = 0;

        for (int i = 0; i < roadCoords.Count - 1; i++)
        {
            var mid = ((roadCoords[i].Lat + roadCoords[i + 1].Lat) / 2,
                       (roadCoords[i].Lng + roadCoords[i + 1].Lng) / 2);
            double dLat = markerLat - mid.Item1;
            double dLng = (markerLng - mid.Item2) * cosLat;
            double d    = Math.Sqrt(dLat * dLat + dLng * dLng);
            if (d < minDist) { minDist = d; nearestIdx = i; }
        }

        var p1 = roadCoords[nearestIdx];
        var p2 = roadCoords[nearestIdx + 1];

        // Cross product: positive → left, negative → right
        double cross = (p2.Lng - p1.Lng) * (markerLat - p1.Lat)
                     - (p2.Lat - p1.Lat) * (markerLng - p1.Lng);

        string side = cross >= 0 ? "left" : "right";

        // Filter entrances by roadDbId inside PostgreSQL via JSONB operators
        // instead of loading all entrances into memory and filtering in C#.
        var usedNumbers = new HashSet<int>();
        var conn        = db.Database.GetDbConnection();

        // Guard against already-open pooled connection
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
    // Delegates to IScatteredAreaService — SQL lives in one place.

    [HttpPost("/api/areas/refresh-scattered")]
    public async Task<IActionResult> RefreshScattered()
    {
        await scatteredService.RefreshAsync(CurrentUserId, CurrentCommuneId);
        return Ok(new ScatteredRefreshResponse(true, null, "Scattered area recomputed."));
    }
}
