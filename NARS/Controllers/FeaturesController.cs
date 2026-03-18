using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Tags("Features")]
public class FeaturesController(AppDbContext db, IScatteredAreaService scatteredService) : NarsControllerBase
{
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
                Layers: new[] { new LayerOption(FeatureTypes.PublicBuildingLayers.Default, "Public Building") }),
            new(Key: FeatureTypes.PublicSpace, Label: "Public Space", Icon: "🌳",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Garden, "Garden"),
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Square, "Square"),
                }),
        };
        return Ok(types);
    }

    // ── POST /api/save ────────────────────────────────────────────────────────

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

        var dataJson = body.Data.ToString();

        long? roadId = null;
        if (body.Type == FeatureTypes.HouseEntrance && body.Layer == FeatureTypes.HouseEntranceLayers.Main)
            if (body.Data.TryGetProperty("roadDbId", out var ridEl) && ridEl.TryGetInt64(out var rid))
                roadId = rid;

        long newId;

        switch (body.Type)
        {
            case FeatureTypes.Area:
                var area = new Area { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson };
                db.Areas.Add(area); await db.SaveChangesAsync(); newId = area.Id;
                if (body.Layer is FeatureTypes.AreaLayers.CentralUrban or FeatureTypes.AreaLayers.SecondaryUrban)
                    _ = scatteredService.RefreshAsync(CurrentUserId, CurrentCommuneId);
                break;
            case FeatureTypes.District:
                var dist = new District { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson };
                db.Districts.Add(dist); await db.SaveChangesAsync(); newId = dist.Id;
                break;
            case FeatureTypes.CityCenter:
                var cc = new CityCenter { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson };
                db.CityCenters.Add(cc); await db.SaveChangesAsync(); newId = cc.Id;
                break;
            case FeatureTypes.Road:
                var road = new Road { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson };
                db.Roads.Add(road); await db.SaveChangesAsync(); newId = road.Id;
                break;
            case FeatureTypes.HouseEntrance:
                var ent = new HouseEntrance { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson, RoadId = roadId };
                db.HouseEntrances.Add(ent); await db.SaveChangesAsync(); newId = ent.Id;
                break;
            case FeatureTypes.PublicBuilding:
                var bld = new PublicBuilding { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson };
                db.PublicBuildings.Add(bld); await db.SaveChangesAsync(); newId = bld.Id;
                break;
            case FeatureTypes.PublicSpace:
                var sp = new PublicSpace { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson };
                db.PublicSpaces.Add(sp); await db.SaveChangesAsync(); newId = sp.Id;
                break;
            default:
                var pan = new NamingPanel { UserId = CurrentUserId, Layer = body.Layer, Label = body.Label, Data = dataJson };
                db.NamingPanels.Add(pan); await db.SaveChangesAsync(); newId = pan.Id;
                break;
        }

        db.FeatureRegistry.Add(new FeatureRegistry { Id = newId, FeatureType = body.Type });
        await db.SaveChangesAsync();

        return StatusCode(201, new { success = true, id = newId, message = "Feature saved successfully" });
    }

    // ── GET /api/load ─────────────────────────────────────────────────────────

    [HttpGet("/api/load")]
    public async Task<IActionResult> LoadFeatures()
    {
        var uid  = CurrentUserId;
        var rows = new List<object>();

        rows.AddRange((await db.Areas.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "area")));
        rows.AddRange((await db.Districts.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "district")));
        rows.AddRange((await db.CityCenters.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "city_center")));
        rows.AddRange((await db.Roads.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "road")));
        rows.AddRange((await db.HouseEntrances.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "house_entrance")));
        rows.AddRange((await db.PublicBuildings.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "public_building")));
        rows.AddRange((await db.PublicSpaces.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "public_space")));
        rows.AddRange((await db.NamingPanels.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt).ToListAsync()).Select(f => ToDto(f, "naming_panel")));

        return Ok(rows);
    }

    // ── POST /api/clear ───────────────────────────────────────────────────────
    // Requires { "confirm": true } in the request body as an explicit opt-in
    // guard against accidental data loss (e.g. a stray curl or test harness call).

    [HttpPost("/api/clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearFeatures([FromBody] ClearFeaturesRequest body)
    {
        if (!body.Confirm)
            return BadRequest(new { detail = "Set \"confirm\": true to delete all features." });

        var uid = CurrentUserId;
        int total = 0;
        total += await db.Areas.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        total += await db.Districts.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        total += await db.CityCenters.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        total += await db.Roads.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        total += await db.HouseEntrances.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        total += await db.PublicBuildings.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        total += await db.PublicSpaces.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        total += await db.NamingPanels.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        return Ok(new { success = true, message = $"Deleted {total} features" });
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────────────────────

    [HttpDelete("/api/delete/{featureId:long}")]
    public async Task<IActionResult> DeleteFeature(long featureId)
    {
        var reg = await db.FeatureRegistry.FindAsync(featureId);
        if (reg is null) return NotFound(new { detail = "Feature not found" });

        int deleted = reg.FeatureType switch
        {
            FeatureTypes.Area           => await db.Areas.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
            FeatureTypes.District       => await db.Districts.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
            FeatureTypes.CityCenter     => await db.CityCenters.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
            FeatureTypes.Road           => await db.Roads.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
            FeatureTypes.HouseEntrance  => await db.HouseEntrances.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
            FeatureTypes.PublicBuilding => await db.PublicBuildings.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
            FeatureTypes.PublicSpace    => await db.PublicSpaces.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
            _                          => await db.NamingPanels.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(),
        };

        if (deleted == 0) return NotFound(new { detail = "Feature not found" });

        await db.FeatureRegistry.Where(r => r.Id == featureId).ExecuteDeleteAsync();

        if (reg.FeatureType == FeatureTypes.Area)
            _ = scatteredService.RefreshAsync(CurrentUserId, CurrentCommuneId);

        return Ok(new { success = true, message = "Feature deleted successfully" });
    }

    // ── GET /api/stats ────────────────────────────────────────────────────────

    [HttpGet("/api/stats")]
    public async Task<IActionResult> GetStats()
    {
        var uid = CurrentUserId;
        var stats = new Dictionary<string, object>
        {
            ["area"]            = await db.Areas.CountAsync(f => f.UserId == uid),
            ["district"]        = await db.Districts.CountAsync(f => f.UserId == uid),
            ["city_center"]     = await db.CityCenters.CountAsync(f => f.UserId == uid),
            ["road"]            = await db.Roads.CountAsync(f => f.UserId == uid),
            ["house_entrance"]  = await db.HouseEntrances.CountAsync(f => f.UserId == uid),
            ["public_building"] = await db.PublicBuildings.CountAsync(f => f.UserId == uid),
            ["public_space"]    = await db.PublicSpaces.CountAsync(f => f.UserId == uid),
        };
        stats["total"] = stats.Values.Cast<int>().Sum();
        return Ok(stats);
    }

    // ── PUT /api/update/{id} ──────────────────────────────────────────────────

    [HttpPut("/api/update/{featureId:long}")]
    public async Task<IActionResult> UpdateFeature(long featureId, [FromBody] FeatureUpdateRequest body)
    {
        var reg = await db.FeatureRegistry.FindAsync(featureId);
        if (reg is null) return NotFound(new { detail = "Feature not found" });

        var updatedAt = DateTime.UtcNow;
        int rows = reg.FeatureType switch
        {
            FeatureTypes.Area           => await UpdateEntity(db.Areas,          featureId, body, updatedAt),
            FeatureTypes.District       => await UpdateEntity(db.Districts,      featureId, body, updatedAt),
            FeatureTypes.CityCenter     => await UpdateEntity(db.CityCenters,    featureId, body, updatedAt),
            FeatureTypes.Road           => await UpdateEntity(db.Roads,          featureId, body, updatedAt),
            FeatureTypes.HouseEntrance  => await UpdateHouseEntrance(            featureId, body, updatedAt),
            FeatureTypes.PublicBuilding => await UpdateEntity(db.PublicBuildings,featureId, body, updatedAt),
            FeatureTypes.PublicSpace    => await UpdateEntity(db.PublicSpaces,   featureId, body, updatedAt),
            _                          => await UpdateEntity(db.NamingPanels,   featureId, body, updatedAt),
        };

        if (rows == 0) return NotFound(new { detail = "Feature not found" });
        return Ok(new { success = true, id = featureId, updated_at = updatedAt });
    }

    // ── POST /api/feature-types/custom ───────────────────────────────────────

    [HttpPost("/api/feature-types/custom")]
    public IActionResult AddCustomFeatureType([FromBody] JsonElement body) =>
        Ok(new { success = true, message = "Custom feature type registered successfully." });

    // ── GET /api/load/layer/{layerType} ───────────────────────────────────────

    [HttpGet("/api/load/layer/{layerType}")]
    public async Task<IActionResult> LoadByLayer(string layerType)
    {
        var uid = CurrentUserId;
        var rows = new List<object>();
        rows.AddRange((await db.Areas.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync()).Select(f => ToDto(f, "area")));
        rows.AddRange((await db.Districts.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync()).Select(f => ToDto(f, "district")));
        rows.AddRange((await db.Roads.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync()).Select(f => ToDto(f, "road")));
        rows.AddRange((await db.HouseEntrances.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync()).Select(f => ToDto(f, "house_entrance")));
        rows.AddRange((await db.PublicBuildings.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync()).Select(f => ToDto(f, "public_building")));
        rows.AddRange((await db.PublicSpaces.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync()).Select(f => ToDto(f, "public_space")));
        return Ok(rows);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object ToDto(FeatureBase f, string type) => new
    {
        id         = f.Id,
        type,
        layer      = f.Layer,
        label      = f.Label,
        data       = JsonSerializer.Deserialize<JsonElement>(f.Data),
        created_at = f.CreatedAt.ToString("o"),
        updated_at = f.UpdatedAt?.ToString("o"),
    };

    private async Task<int> UpdateEntity<T>(DbSet<T> set, long id, FeatureUpdateRequest body, DateTime updatedAt)
        where T : FeatureBase
    {
        var entity = await set.FirstOrDefaultAsync(f => f.Id == id && f.UserId == CurrentUserId);
        if (entity is null) return 0;
        if (body.Label is not null) entity.Label = body.Label;
        if (body.Data is not null)  entity.Data  = body.Data.ToString()!;
        entity.UpdatedAt = updatedAt;
        return await db.SaveChangesAsync();
    }

    private async Task<int> UpdateHouseEntrance(long id, FeatureUpdateRequest body, DateTime updatedAt)
    {
        var entity = await db.HouseEntrances.FirstOrDefaultAsync(f => f.Id == id && f.UserId == CurrentUserId);
        if (entity is null) return 0;
        if (body.Label is not null) entity.Label = body.Label;
        if (body.Data is not null)
        {
            entity.Data = body.Data.ToString()!;
            if (body.Data.Value.TryGetProperty("roadDbId", out var ridEl) && ridEl.TryGetInt64(out var rid))
                entity.RoadId = rid;
        }
        entity.UpdatedAt = updatedAt;
        return await db.SaveChangesAsync();
    }
}
