using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Controllers;

// fix #2 & #9: Extends NarsControllerBase which carries [Authorize] and exposes
// CurrentUserId / CurrentCommuneId from claims — no more manual RequireAuth().
[ApiController]
[Tags("Features")]
public class FeaturesController(AppDbContext db, IDbContextFactory<AppDbContext> dbFactory) : NarsControllerBase
{
    // ── GET /api/feature-types ────────────────────────────────

    [HttpGet("/api/feature-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFeatureTypes()
    {
        var types = new List<FeatureTypeDefinition>
        {
            new(Key: FeatureTypes.Area, Label: "Area", Icon: "⬟",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.AreaLayers.CentralUrban,   "Central Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.SecondaryUrban, "Secondary Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.Scattered,      "Scattered Area"),
                }),
            new(Key: FeatureTypes.Road, Label: "Road", Icon: "🛣️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.RoadLayers.Boulevard, "Boulevard", "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Avenue,    "Avenue",    "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Street,    "Street",    "secondary"),
                    new LayerOption(FeatureTypes.RoadLayers.Drive,     "Drive",     "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Lane,      "Lane",      "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.CulDeSac,  "Cul-de-sac","tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Way,       "Way",       "tertiary"),
                }),
            new(Key: FeatureTypes.District, Label: "District", Icon: "🏘️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.DistrictLayers.HousingEstate,      "Housing Estate"),
                    new LayerOption(FeatureTypes.DistrictLayers.UrbanPole,          "Urban Pole"),
                    new LayerOption(FeatureTypes.DistrictLayers.District,           "District"),
                    new LayerOption(FeatureTypes.DistrictLayers.TradActivitiesZone, "Trad. Activities Zone"),
                    new LayerOption(FeatureTypes.DistrictLayers.IndustryZone,       "Industry Zone"),
                }),
            new(Key: FeatureTypes.HouseEntrance, Label: "House Entrance", Icon: "🚪",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Main,      "Main Entrance"),
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Secondary, "Secondary Entrance"),
                }),
            new(Key: FeatureTypes.PublicBuilding, Label: "Public Building", Icon: "🏛️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Default, "Public Building"),
                }),
            new(Key: FeatureTypes.PublicSpace, Label: "Public Space", Icon: "🌳",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Garden, "Garden"),
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Square, "Square"),
                }),
        };

        return Ok(types);
    }

    // ── POST /api/save ────────────────────────────────────────

    [HttpPost("/api/save")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SaveFeature([FromBody] FeatureSaveRequest body)
    {
        if (!FeatureTypes.All.Contains(body.Type))
            return BadRequest(new { detail = $"Unknown feature type '{body.Type}'." });

        if (!FeatureTypes.IsValidLayer(body.Type, body.Layer))
            return BadRequest(new { detail = $"Layer '{body.Layer}' is not valid for type '{body.Type}'." });

        var feature = new Feature
        {
            UserId = CurrentUserId,
            Type   = body.Type,
            Layer  = body.Layer,
            Label  = body.Label,
            Data   = body.Data.ToString(),
        };

        db.Features.Add(feature);
        await db.SaveChangesAsync();

        if (body.Type == FeatureTypes.Area &&
            (body.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             body.Layer == FeatureTypes.AreaLayers.SecondaryUrban))
        {
            // Capture values now — HttpContext is disposed before the task completes.
            int uid = CurrentUserId, cid = CurrentCommuneId;
            _ = TriggerScatteredRefreshAsync(uid, cid);
        }

        return StatusCode(201, new { success = true, id = feature.Id, message = "Feature saved successfully" });
    }

    // ── GET /api/load ─────────────────────────────────────────

    [HttpGet("/api/load")]
    public async Task<IActionResult> LoadFeatures()
    {
        var features = await db.Features
            .Where(f => f.UserId == CurrentUserId)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        return Ok(features.Select(ToDto));
    }

    // ── POST /api/clear ───────────────────────────────────────

    [HttpPost("/api/clear")]
    public async Task<IActionResult> ClearFeatures()
    {
        var count = await db.Features
            .Where(f => f.UserId == CurrentUserId)
            .ExecuteDeleteAsync();

        return Ok(new { success = true, message = $"Deleted {count} features" });
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────

    [HttpDelete("/api/delete/{featureId:int}")]
    public async Task<IActionResult> DeleteFeature(int featureId)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f =>
            f.Id == featureId && f.UserId == CurrentUserId);

        if (feature is null)
            return NotFound(new { detail = "Feature not found" });

        bool wasUrbanArea = feature.Type == FeatureTypes.Area &&
            (feature.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             feature.Layer == FeatureTypes.AreaLayers.SecondaryUrban);

        db.Features.Remove(feature);
        await db.SaveChangesAsync();

        if (wasUrbanArea)
        {
            int uid = CurrentUserId, cid = CurrentCommuneId;
            _ = TriggerScatteredRefreshAsync(uid, cid);
        }

        return Ok(new { success = true, message = "Feature deleted successfully" });
    }

    // ── GET /api/stats ────────────────────────────────────────

    [HttpGet("/api/stats")]
    public async Task<IActionResult> GetStats()
    {
        var groups = await db.Features
            .Where(f => f.UserId == CurrentUserId)
            .GroupBy(f => f.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = groups.Sum(g => g.Count);
        var stats = groups.ToDictionary(g => g.Type, g => (object)g.Count);
        stats["total"] = total;

        return Ok(stats);
    }

    // ── GET /api/load/layer/{layerType} ───────────────────────

    [HttpGet("/api/load/layer/{layerType}")]
    public async Task<IActionResult> LoadByLayer(string layerType)
    {
        var features = await db.Features
            .Where(f => f.UserId == CurrentUserId && f.Layer == layerType)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        return Ok(features.Select(ToDto));
    }

    // ── GET /api/load/type/{featureType} ──────────────────────

    [HttpGet("/api/load/type/{featureType}")]
    public async Task<IActionResult> LoadByType(string featureType)
    {
        var features = await db.Features
            .Where(f => f.UserId == CurrentUserId && f.Type == featureType)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        return Ok(features.Select(ToDto));
    }

    // ── PUT /api/update/{id} ─────────────────────────────────

    [HttpPut("/api/update/{featureId:int}")]
    public async Task<IActionResult> UpdateFeature(int featureId, [FromBody] FeatureUpdateRequest body)
    {
        var feature = await db.Features.FirstOrDefaultAsync(f =>
            f.Id == featureId && f.UserId == CurrentUserId);

        if (feature is null)
            return NotFound(new { detail = "Feature not found" });

        if (body.Label is not null)
            feature.Label = body.Label;

        if (body.Data is not null)
            feature.Data = body.Data.ToString()!;

        feature.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Ok(new { success = true, id = feature.Id, updated_at = feature.UpdatedAt });
    }

    // ── Helpers ───────────────────────────────────────────────

    private static object ToDto(Feature f) => new
    {
        id         = f.Id,
        type       = f.Type,
        layer      = f.Layer,
        label      = f.Label,
        data       = JsonSerializer.Deserialize<JsonElement>(f.Data),
        created_at = f.CreatedAt.ToString("o"),
        updated_at = f.UpdatedAt?.ToString("o"),
    };

    // ── Trigger scattered area recomputation ──────────────────
    // Fire-and-forget: called after any urban area save/delete.
    // Uses IDbContextFactory so it owns its context and connection
    // independently of the request scope, which may be disposed before
    // this task completes. Never borrows the request-scoped `db`.
    private async Task TriggerScatteredRefreshAsync(int userId, int communeId)
    {
        try
        {
            // Create a fully independent DbContext — not shared with the request.
            await using var ownedDb = await dbFactory.CreateDbContextAsync();
            var conn = ownedDb.Database.GetDbConnection();
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
                        FROM features f
                        WHERE f.user_id = @uid
                          AND f.type   = 'area'
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

                AddParam(cmd, "@cid", communeId);
                AddParam(cmd, "@uid", userId);

                // CommandBehavior.SequentialAccess streams the large GeoJSON column
                // in chunks instead of buffering it in Npgsql's 8 KB internal buffer.
                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
                if (await reader.ReadAsync() && !await reader.IsDBNullAsync(0))
                    scatteredGeoJson = await reader.GetTextReader(0).ReadToEndAsync();
            }
            finally { await conn.CloseAsync(); }

            if (scatteredGeoJson is null) return;

            await ownedDb.Features
                .Where(f => f.UserId == userId &&
                            f.Type   == FeatureTypes.Area &&
                            f.Layer  == FeatureTypes.AreaLayers.Scattered)
                .ExecuteDeleteAsync();

            ownedDb.Features.Add(new Feature
            {
                UserId = userId,
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
            });
            await ownedDb.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RefreshScattered] Error: {ex.Message}");
        }
    }
}
