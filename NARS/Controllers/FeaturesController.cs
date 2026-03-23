using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// CRUD operations on saved map features: save, load, update, delete, clear, stats.
/// Feature-type metadata and layer queries live in FeatureCatalogController.
/// </summary>
[ApiController]
[Tags("Features")]
public class FeaturesController(
    AppDbContext db,
    IDbContextFactory<AppDbContext> dbFactory,
    IScatteredAreaService scatteredService) : NarsControllerBase
{
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

        // Wrap the feature insert + registry insert in a single transaction so
        // a failure on the second save never leaves an orphaned feature row.
        using var tx = await db.Database.BeginTransactionAsync();

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
        await tx.CommitAsync();

        return StatusCode(201, new { success = true, id = newId, message = "Feature saved successfully" });
    }

    // ── GET /api/load ─────────────────────────────────────────────────────────

    [HttpGet("/api/load")]
    public async Task<IActionResult> LoadFeatures()
    {
        var uid = CurrentUserId;

        // EF Core DbContext is NOT thread-safe — parallel queries must each own
        // their own context instance. dbFactory.CreateDbContextAsync() pulls a
        // fresh, independently-owned context from the same pool configuration.
        // Each 'await using' disposes its context when the task completes.
        static async Task<List<T>> Query<T>(
            IDbContextFactory<AppDbContext> f,
            Func<AppDbContext, IQueryable<T>> selector) where T : class
        {
            await using var ctx = await f.CreateDbContextAsync();
            return await selector(ctx).ToListAsync();
        }

        var tAreas     = Query(dbFactory, c => c.Areas.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));
        var tDistricts = Query(dbFactory, c => c.Districts.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));
        var tCCenters  = Query(dbFactory, c => c.CityCenters.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));
        var tRoads     = Query(dbFactory, c => c.Roads.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));
        var tEntrances = Query(dbFactory, c => c.HouseEntrances.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));
        var tBuildings = Query(dbFactory, c => c.PublicBuildings.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));
        var tSpaces    = Query(dbFactory, c => c.PublicSpaces.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));
        var tPanels    = Query(dbFactory, c => c.NamingPanels.Where(f => f.UserId == uid).OrderBy(f => f.CreatedAt));

        await Task.WhenAll(tAreas, tDistricts, tCCenters, tRoads, tEntrances, tBuildings, tSpaces, tPanels);

        var rows = new List<object>();
        rows.AddRange(tAreas.Result.Select(f     => ToDto(f, "area")));
        rows.AddRange(tDistricts.Result.Select(f => ToDto(f, "district")));
        rows.AddRange(tCCenters.Result.Select(f  => ToDto(f, "city_center")));
        rows.AddRange(tRoads.Result.Select(f     => ToDto(f, "road")));
        rows.AddRange(tEntrances.Result.Select(f => ToDto(f, "house_entrance")));
        rows.AddRange(tBuildings.Result.Select(f => ToDto(f, "public_building")));
        rows.AddRange(tSpaces.Result.Select(f    => ToDto(f, "public_space")));
        rows.AddRange(tPanels.Result.Select(f    => ToDto(f, "naming_panel")));

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

        // Remove all registry entries whose IDs no longer exist in any feature table.
        // (Simpler than a user_id column on feature_registry — IDs are globally unique.)
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM feature_registry
            WHERE id NOT IN (
                SELECT id FROM areas          UNION ALL
                SELECT id FROM districts      UNION ALL
                SELECT id FROM city_centers   UNION ALL
                SELECT id FROM roads          UNION ALL
                SELECT id FROM house_entrances UNION ALL
                SELECT id FROM public_buildings UNION ALL
                SELECT id FROM public_spaces  UNION ALL
                SELECT id FROM naming_panels
            )");

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

        // Each CountAsync runs on its own context — same reason as LoadFeatures.
        static async Task<int> Count<T>(
            IDbContextFactory<AppDbContext> f,
            Func<AppDbContext, IQueryable<T>> selector) where T : class
        {
            await using var ctx = await f.CreateDbContextAsync();
            return await selector(ctx).CountAsync();
        }

        var tAreas     = Count(dbFactory, c => c.Areas.Where(f => f.UserId == uid));
        var tDistricts = Count(dbFactory, c => c.Districts.Where(f => f.UserId == uid));
        var tCCenters  = Count(dbFactory, c => c.CityCenters.Where(f => f.UserId == uid));
        var tRoads     = Count(dbFactory, c => c.Roads.Where(f => f.UserId == uid));
        var tEntrances = Count(dbFactory, c => c.HouseEntrances.Where(f => f.UserId == uid));
        var tBuildings = Count(dbFactory, c => c.PublicBuildings.Where(f => f.UserId == uid));
        var tSpaces    = Count(dbFactory, c => c.PublicSpaces.Where(f => f.UserId == uid));

        await Task.WhenAll(tAreas, tDistricts, tCCenters, tRoads, tEntrances, tBuildings, tSpaces);

        return Ok(new
        {
            area            = tAreas.Result,
            district        = tDistricts.Result,
            city_center     = tCCenters.Result,
            road            = tRoads.Result,
            house_entrance  = tEntrances.Result,
            public_building = tBuildings.Result,
            public_space    = tSpaces.Result,
            total           = tAreas.Result + tDistricts.Result + tCCenters.Result +
                              tRoads.Result + tEntrances.Result + tBuildings.Result + tSpaces.Result,
        });
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
