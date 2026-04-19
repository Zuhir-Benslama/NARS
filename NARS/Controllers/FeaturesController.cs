using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
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
    IScatteredAreaService scatteredService,
    IBackgroundTaskQueue bgQueue,
    ILogger<FeaturesController> logger) : NarsControllerBase
{
    // Maximum allowed size for feature data JSON payloads (512 KB).
    private const int MaxFeatureDataSize = 524_288;

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

        // Prevent manual creation of scattered areas — these are auto-computed
        if (body.Type == FeatureTypes.Area && body.Layer == FeatureTypes.AreaLayers.Scattered)
            return BadRequest(new { detail = "Scattered areas are auto-computed and cannot be saved manually." });

        // Guard against oversized JSON payloads (max ~500 KB per feature)
        var rawJson = body.Data.GetRawText();
        if (rawJson.Length > MaxFeatureDataSize)
            return BadRequest(new { detail = "Feature data is too large (max 512 KB)." });

        var dataJson = rawJson;

        Guid? roadId = null;
        if (body.Type == FeatureTypes.HouseEntrance && body.Layer == FeatureTypes.HouseEntranceLayers.Main)
            if (body.Data.TryGetProperty("roadDbId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String && Guid.TryParse(ridEl.GetString(), out var rid))
                roadId = rid;

        // Validate that the referenced road exists and belongs to the current user.
        if (roadId.HasValue && !await db.Roads.AnyAsync(r => r.Id == roadId.Value && r.UserId == CurrentUserId))
            return BadRequest(new { detail = "Referenced road not found." });

        Guid newId = Guid.CreateVersion7();

        // Use the registry to create the entity — no more 8-case switch statement
        var entity = FeatureTypeRegistry.CreateEntity(body.Type, newId, CurrentUserId, body.Layer, body.Label, dataJson);
        if (entity is null)
            return BadRequest(new { detail = $"Unknown feature type '{body.Type}'." });

        // HouseEntrance-specific: set RoadId on the concrete type
        if (entity is HouseEntrance entrance)
            entrance.RoadId = roadId;

        // Wrap the feature insert + registry insert in a single transaction
        await using var tx = await db.Database.BeginTransactionAsync();

        var entry = FeatureTypeRegistry.AddToDbContext(db, entity);
        if (entry is null)
            return BadRequest(new { detail = $"Unknown feature type '{body.Type}'." });
        await db.SaveChangesAsync();

        db.FeatureRegistry.Add(new FeatureRegistry { Id = newId, FeatureType = body.Type });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        if (body.Type == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return StatusCode(201, new { success = true, id = newId.ToString(), message = "Feature saved successfully" });
    }

    // ── GET /api/load ─────────────────────────────────────────────────────────
    // Supports pagination via ?skip=0&take=100 query parameters.
    // Maximum page size is 500 to prevent oversized responses.
    // Uses UNION ALL across all feature tables so Skip/Take applies to the
    // combined result — not per-table (which would return up to 8x the page).

    [HttpGet("/api/load")]
    public async Task<IActionResult> LoadFeatures([FromQuery] int skip = 0, [FromQuery] int take = 1000)
    {
        // Cap page size to prevent memory exhaustion and oversized responses.
        take = Math.Clamp(take, 1, 2000);

        var (features, totalCount) = await FeatureQueryHelper.LoadAllFeaturesAsync(
            db.Database.GetDbConnection(), CurrentUserId, skip, take);

        return Ok(new
        {
            features,
            count = totalCount,
            skip,
            take,
        });
    }

    // ── POST /api/clear ───────────────────────────────────────────────────────

    [HttpPost("/api/clear")]
    [EnableRateLimiting("clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearFeatures([FromBody] ClearFeaturesRequest body)
    {
        if (!body.Confirm)
            return BadRequest(new { detail = "Set \"confirm\": true to delete all features." });

        var uid = CurrentUserId;
        int total = 0;

        await using var tx = await db.Database.BeginTransactionAsync();

        // Use the registry to iterate over all feature types — no hardcoded list
        foreach (var type in FeatureTypeRegistry.GetAllTypes())
        {
            var dbSet = FeatureTypeRegistry.GetDbSet(db, type)!;
            total += await dbSet.Where(f => f.UserId == uid).ExecuteDeleteAsync();
        }

        // Build the orphan-cleanup UNION ALL from the registry so it automatically
        // stays in sync when new feature types are added — no hardcoded table list.
        // Table names come from FeatureTypeRegistry constants (developer-owned, never
        // user-supplied), so interpolation here is safe. ExecuteSqlAsync cannot be used
        // because SQL does not allow table names as parameters.
#pragma warning disable EF1002
        var unionAll = string.Join(
            " UNION ALL ",
            FeatureTypeRegistry.GetAllTableNames().Select(t => $"SELECT id FROM {t}"));

        await db.Database.ExecuteSqlRawAsync(
            $"DELETE FROM feature_registry WHERE id NOT IN ({unionAll})");
#pragma warning restore EF1002

        await tx.CommitAsync();

        return Ok(new { success = true, message = $"Deleted {total} features" });
    }

    // ── GET /api/stats ────────────────────────────────────────────────────────

    [HttpGet("/api/stats")]
    public async Task<IActionResult> GetStats()
    {
        var uid = CurrentUserId;

        // Single UNION ALL query instead of 8 sequential round-trips.
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 'area' AS type, COUNT(*) FROM areas WHERE user_id = @uid UNION ALL
                SELECT 'district', COUNT(*) FROM districts WHERE user_id = @uid UNION ALL
                SELECT 'city_center', COUNT(*) FROM city_centers WHERE user_id = @uid UNION ALL
                SELECT 'road', COUNT(*) FROM roads WHERE user_id = @uid UNION ALL
                SELECT 'house_entrance', COUNT(*) FROM house_entrances WHERE user_id = @uid UNION ALL
                SELECT 'public_building', COUNT(*) FROM public_buildings WHERE user_id = @uid UNION ALL
                SELECT 'public_space', COUNT(*) FROM public_spaces WHERE user_id = @uid UNION ALL
                SELECT 'naming_panel', COUNT(*) FROM naming_panels WHERE user_id = @uid
                """;
            var uidParam = cmd.CreateParameter();
            uidParam.ParameterName = "@uid";
            uidParam.Value = uid;
            cmd.Parameters.Add(uidParam);

            var counts = new Dictionary<string, long>(8);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                counts[reader.GetString(0)] = reader.GetInt64(1);
            }

            long GetCount(string key) => counts.TryGetValue(key, out var v) ? v : 0;
            var total = counts.Values.Sum();

            return Ok(new
            {
                area = GetCount("area"),
                district = GetCount("district"),
                city_center = GetCount("city_center"),
                road = GetCount("road"),
                house_entrance = GetCount("house_entrance"),
                public_building = GetCount("public_building"),
                public_space = GetCount("public_space"),
                naming_panel = GetCount("naming_panel"),
                total,
            });
        }
        finally
        {
            if (!wasOpen && conn.State == System.Data.ConnectionState.Open)
                await conn.CloseAsync();
        }
    }

    // ── GET /api/scattered-status ─────────────────────────────────────────────

    [HttpGet("/api/scattered-status")]
    public IActionResult GetScatteredStatus()
    {
        var error = scatteredService.LastError;
        return Ok(new
        {
            lastErrorTime = error?.Timestamp.ToString("o"),
            lastErrorMessage = error?.Message,
            hasError = error.HasValue,
        });
    }

    // ── PUT /api/update/{id} ──────────────────────────────────────────────────

    [HttpPut("/api/update/{featureId:guid}")]
    public async Task<IActionResult> UpdateFeature(Guid featureId, [FromBody] FeatureUpdateRequest body)
    {
        var reg = await db.FeatureRegistry.FindAsync(featureId);
        if (reg is null) return NotFound(new { detail = "Feature not found" });

        // Guard against oversized JSON payloads (max ~500 KB per feature)
        if (body.Data is JsonElement dataElement)
        {
            var rawJson = dataElement.GetRawText();
            if (rawJson.Length > MaxFeatureDataSize)
                return BadRequest(new { detail = "Feature data is too large (max 512 KB)." });
        }

        var updatedAt = DateTime.UtcNow;

        // HouseEntrance needs special handling for RoadId extraction from data
        if (reg.FeatureType == FeatureTypes.HouseEntrance)
        {
            int rows = await UpdateHouseEntrance(featureId, body, updatedAt);
            if (rows == 0) return NotFound(new { detail = "Feature not found" });
            return Ok(new { success = true, id = featureId.ToString(), updated_at = updatedAt });
        }

        var dbSet = FeatureTypeRegistry.GetDbSet(db, reg.FeatureType);
        if (dbSet is null)
            return BadRequest(new { detail = $"Unknown feature type in registry: {reg.FeatureType}" });

        int updated = await UpdateEntityGeneric(dbSet, featureId, body, updatedAt);
        if (updated == 0) return NotFound(new { detail = "Feature not found" });

        if (reg.FeatureType == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return Ok(new { success = true, id = featureId.ToString(), updated_at = updatedAt });
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────────────────────

    [HttpDelete("/api/delete/{featureId:guid}")]
    public async Task<IActionResult> DeleteFeature(Guid featureId)
    {
        var reg = await db.FeatureRegistry.FindAsync(featureId);
        if (reg is null) return NotFound(new { detail = "Feature not found" });

        await using var tx = await db.Database.BeginTransactionAsync();

        var dbSet = FeatureTypeRegistry.GetDbSet(db, reg.FeatureType);
        if (dbSet is null)
        {
            await tx.RollbackAsync();
            return BadRequest(new { detail = $"Unknown feature type in registry: {reg.FeatureType}" });
        }

        int deleted = await dbSet.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync();

        if (deleted == 0)
        {
            await tx.RollbackAsync();
            return NotFound(new { detail = "Feature not found" });
        }

        await db.FeatureRegistry.Where(r => r.Id == featureId).ExecuteDeleteAsync();
        await tx.CommitAsync();

        if (reg.FeatureType == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return Ok(new { success = true, message = "Feature deleted successfully" });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<int> UpdateEntityGeneric(IQueryable<FeatureBase> query, Guid id, FeatureUpdateRequest body, DateTime updatedAt)
    {
        var entity = await query.FirstOrDefaultAsync(f => f.Id == id && f.UserId == CurrentUserId);
        if (entity is null) return 0;
        if (body.Label is not null) entity.Label = body.Label;
        if (body.Data is not null)
        {
            if (body.Data.Value.ValueKind == JsonValueKind.String)
                entity.Data = body.Data.Value.GetString()!;
            else
                entity.Data = body.Data.Value.GetRawText();
        }
        entity.UpdatedAt = updatedAt;
        return await db.SaveChangesAsync();
    }

    private async Task<int> UpdateHouseEntrance(Guid id, FeatureUpdateRequest body, DateTime updatedAt)
    {
        var entity = await db.HouseEntrances.FirstOrDefaultAsync(f => f.Id == id && f.UserId == CurrentUserId);
        if (entity is null) return 0;
        if (body.Label is not null) entity.Label = body.Label;
        if (body.Data is not null)
        {
            var dataJson = body.Data.Value.ValueKind == JsonValueKind.String
                ? body.Data.Value.GetString()!
                : body.Data.Value.GetRawText();
            entity.Data = dataJson;

            if (body.Data.Value.TryGetProperty("roadDbId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String && Guid.TryParse(ridEl.GetString(), out var rid))
                entity.RoadId = rid;
        }
        entity.UpdatedAt = updatedAt;
        return await db.SaveChangesAsync();
    }

    /// <summary>
    /// Schedules a background refresh of the scattered area geometry.
    /// Used after any manual change to the 'area' feature set.
    /// </summary>
    private async ValueTask QueueScatteredRefresh()
    {
        await bgQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
        {
            try
            {
                var svc = sp.GetRequiredService<IScatteredAreaService>();
                await svc.RefreshAsync(CurrentUserId, CurrentCommuneId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background refresh of scattered area failed");
            }
        });
    }
}
