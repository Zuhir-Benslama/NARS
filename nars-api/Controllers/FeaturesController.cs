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
[Route("/api")]
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

    [HttpPost("save")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SaveFeature([FromBody] FeatureSaveRequest body, CancellationToken cancellationToken = default)
    {
        if (!FeatureTypes.AllTypes.Contains(body.Type))
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
        if (body.Type == FeatureTypes.HouseEntrance && body.Layer == FeatureTypes.HouseEntranceLayers.Main
            && body.Data.TryGetProperty("roadDbId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String && Guid.TryParse(ridEl.GetString(), out var rid))
            roadId = rid;

        // Validate that the referenced road exists and belongs to the current user.
        if (roadId.HasValue && !await db.Roads.AnyAsync(r => r.Id == roadId.Value && r.UserId == CurrentUserId, cancellationToken))
            return BadRequest(new { detail = "Referenced road not found." });

        Guid newId = Guid.CreateVersion7();

        // Use the registry to create the entity — no more 8-case switch statement
        var entity = FeatureTypeRegistry.CreateEntity(body.Type, newId, RequiredCurrentUserId, body.Layer, body.Label, dataJson);
        if (entity is null)
            return BadRequest(new { detail = $"Unknown feature type '{body.Type}'." });

        // HouseEntrance-specific: set RoadId on the concrete type
        if (entity is HouseEntrance entrance)
            entrance.RoadId = roadId;

        // Wrap the feature insert + registry insert in a single transaction
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var entry = FeatureTypeRegistry.AddToDbContext(db, entity);
        if (entry is null)
            return BadRequest(new { detail = $"Unknown feature type '{body.Type}'." });
        await db.SaveChangesAsync(cancellationToken);

        db.FeatureRegistry.Add(new FeatureRegistry { Id = newId, FeatureType = body.Type });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (body.Type == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return StatusCode(201, new SaveFeatureResponse(Success: true, Id: newId.ToString(), Message: "Feature saved successfully"));
    }

    // ── GET /api/load ─────────────────────────────────────────────────────────
    // Supports pagination via ?skip=0&take=100 query parameters.
    // Default page size is 1000, maximum is 2000 to prevent oversized responses.
    // Uses UNION ALL across all feature tables so Skip/Take applies to the
    // combined result — not per-table (which would return up to 8x the page).

    [HttpGet("load")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoadFeatures([FromQuery] int skip = 0, [FromQuery] int take = 1000, CancellationToken cancellationToken = default)
    {
        // Cap page size to prevent memory exhaustion and oversized responses.
        take = Math.Clamp(take, 1, 2000);

        var (features, totalCount) = await FeatureQueryHelper.LoadAllFeaturesAsync(
            db.Database.GetDbConnection(), RequiredCurrentUserId, skip, take);

        return Ok(new LoadFeaturesResponse(
            Features: features,
            Count: totalCount,
            Skip: skip,
            Take: take
        ));
    }

    // ── POST /api/clear ───────────────────────────────────────────────────────

    [HttpPost("clear")]
    [EnableRateLimiting(RateLimitPolicies.Clear)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearFeatures([FromBody] ClearFeaturesRequest body, CancellationToken cancellationToken = default)
    {
        if (!body.Confirm)
            return BadRequest(new { detail = "Set \"confirm\": true to delete all features." });

        var uid = CurrentUserId;
        int total = 0;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Use the registry to iterate over all feature types — no hardcoded list
        foreach (var type in FeatureTypeRegistry.GetAllTypes())
        {
            var dbSet = FeatureTypeRegistry.GetDbSet(db, type)!;
            total += await dbSet.Where(f => f.UserId == uid).ExecuteDeleteAsync(cancellationToken);
        }

        // Collect all remaining feature IDs across all feature tables
        // then delete orphaned registry entries (entries whose ID doesn't
        // exist in any feature table).
        var allIds = new HashSet<Guid>();
        foreach (var type in FeatureTypeRegistry.GetAllTypes())
        {
            var dbSet = FeatureTypeRegistry.GetDbSet(db, type)!;
            var ids = await dbSet.Select(f => f.Id).ToListAsync(cancellationToken);
            foreach (var id in ids) allIds.Add(id);
        }

        await db.FeatureRegistry
            .Where(r => !allIds.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Ok(new ActionResponse(Success: true, Message: $"Deleted {total} features"));
    }

    // ── GET /api/stats ────────────────────────────────────────────────────────

    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken = default)
    {
        var uid = CurrentUserId;

        // Single UNION ALL query built from FeatureTypeRegistry instead of
        // hardcoded per-type SELECTs — adding a new type auto-extends this query.
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();

        try
        {
            await using var cmd = conn.CreateCommand();
            var sql = new System.Text.StringBuilder();
            var descriptors = FeatureTypeRegistry.GetAllDescriptors();
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (i > 0) sql.Append(" UNION ALL ");
                sql.Append($"SELECT '{descriptors[i].Type}' AS type, COUNT(*) FROM {descriptors[i].TableName} WHERE user_id = @uid");
            }
            cmd.CommandText = sql.ToString();
            var uidParam = cmd.CreateParameter();
            uidParam.ParameterName = "@uid";
            uidParam.Value = uid;
            cmd.Parameters.Add(uidParam);

            var counts = new Dictionary<string, long>(descriptors.Count);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                counts[reader.GetString(0)] = reader.GetInt64(1);
            }

            long GetCount(string key) => counts.TryGetValue(key, out var v) ? v : 0;
            var total = counts.Values.Sum();

            return Ok(new FeatureStatsResponse(
                Area: GetCount(FeatureTypes.Area),
                District: GetCount(FeatureTypes.District),
                CityCenter: GetCount(FeatureTypes.CityCenter),
                Road: GetCount(FeatureTypes.Road),
                HouseEntrance: GetCount(FeatureTypes.HouseEntrance),
                PublicBuilding: GetCount(FeatureTypes.PublicBuilding),
                PublicSpace: GetCount(FeatureTypes.PublicSpace),
                NamingPanel: GetCount(FeatureTypes.NamingPanel),
                Total: total
            ));
        }
        finally
        {
            if (!wasOpen && conn.State == System.Data.ConnectionState.Open)
                await conn.CloseAsync();
        }
    }

    // ── GET /api/scattered-status ─────────────────────────────────────────────

    [HttpGet("scattered-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetScatteredStatus()
    {
        var error = scatteredService.LastError;
        return Ok(new ScatteredStatusResponse(
            LastErrorTime: error?.Timestamp.ToString("o"),
            LastErrorMessage: error?.Message,
            HasError: error.HasValue
        ));
    }

    // ── PUT /api/update/{id} ──────────────────────────────────────────────────

    [HttpPut("update/{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFeature(Guid featureId, [FromBody] FeatureUpdateRequest body, CancellationToken cancellationToken = default)
    {
        var reg = await db.FeatureRegistry.FindAsync([featureId], cancellationToken);
        if (reg is null) return NotFound(new { detail = "Feature not found" });

        // Verify ownership — prevent users from updating other users' features
        var owned = await FeatureTypeRegistry.GetDbSet(db, reg.FeatureType)!
            .AnyAsync(f => f.Id == featureId && f.UserId == CurrentUserId, cancellationToken);
        if (!owned) return NotFound(new { detail = "Feature not found" });

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
            int rows = await UpdateHouseEntrance(featureId, body, updatedAt, cancellationToken);
            if (rows == 0) return NotFound(new { detail = "Feature not found" });
            return Ok(new UpdateFeatureResponse(Success: true, Id: featureId.ToString(), UpdatedAt: updatedAt));
        }

        var dbSet = FeatureTypeRegistry.GetDbSet(db, reg.FeatureType);
        if (dbSet is null)
            return BadRequest(new { detail = $"Unknown feature type in registry: {reg.FeatureType}" });

        int updated = await UpdateEntityGeneric(dbSet, featureId, body, updatedAt, cancellationToken);
        if (updated == 0) return NotFound(new { detail = "Feature not found" });

        if (reg.FeatureType == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return Ok(new UpdateFeatureResponse(Success: true, Id: featureId.ToString(), UpdatedAt: updatedAt));
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────────────────────

    [HttpDelete("delete/{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFeature(Guid featureId, CancellationToken cancellationToken = default)
    {
        var reg = await db.FeatureRegistry.FindAsync([featureId], cancellationToken);
        if (reg is null) return NotFound(new { detail = "Feature not found" });

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var dbSet = FeatureTypeRegistry.GetDbSet(db, reg.FeatureType);
        if (dbSet is null)
        {
            await tx.RollbackAsync();
            return BadRequest(new { detail = $"Unknown feature type in registry: {reg.FeatureType}" });
        }

        int deleted = await dbSet.Where(f => f.Id == featureId && f.UserId == CurrentUserId).ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return NotFound(new { detail = "Feature not found" });
        }

        await db.FeatureRegistry.Where(r => r.Id == featureId).ExecuteDeleteAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (reg.FeatureType == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return Ok(new ActionResponse(Success: true, Message: "Feature deleted successfully"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<int> UpdateEntityGeneric(IQueryable<FeatureBase> query, Guid id, FeatureUpdateRequest body, DateTime updatedAt, CancellationToken cancellationToken = default)
    {
        var rows = await query
            .Where(f => f.Id == id && f.UserId == CurrentUserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.UpdatedAt, updatedAt)
            , cancellationToken);

        if (rows == 0) return 0;

        if (body.Label is not null)
        {
            await query
                .Where(f => f.Id == id && f.UserId == CurrentUserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.Label, body.Label)
                , cancellationToken);
        }

        if (body.Data is not null)
        {
            var dataStr = body.Data.Value.ValueKind == JsonValueKind.String
                ? body.Data.Value.GetString()!
                : body.Data.Value.GetRawText();
            await query
                .Where(f => f.Id == id && f.UserId == CurrentUserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.Data, dataStr)
                , cancellationToken);
        }

        return rows;
    }

    private async Task<int> UpdateHouseEntrance(Guid id, FeatureUpdateRequest body, DateTime updatedAt, CancellationToken cancellationToken = default)
    {
        var rows = await db.HouseEntrances
            .Where(f => f.Id == id && f.UserId == CurrentUserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.UpdatedAt, updatedAt)
            , cancellationToken);

        if (rows == 0) return 0;

        if (body.Label is not null)
        {
            await db.HouseEntrances
                .Where(f => f.Id == id && f.UserId == CurrentUserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.Label, body.Label)
                , cancellationToken);
        }

        if (body.Data is not null)
        {
            var dataStr = body.Data.Value.ValueKind == JsonValueKind.String
                ? body.Data.Value.GetString()!
                : body.Data.Value.GetRawText();
            await db.HouseEntrances
                .Where(f => f.Id == id && f.UserId == CurrentUserId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.Data, dataStr)
                , cancellationToken);

            if (body.Data.Value.TryGetProperty("roadDbId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String && Guid.TryParse(ridEl.GetString(), out var rid))
            {
                await db.HouseEntrances
                    .Where(f => f.Id == id && f.UserId == CurrentUserId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(f => f.RoadId, rid)
                    , cancellationToken);
            }
        }

        return rows;
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
                await svc.RefreshAsync(RequiredCurrentUserId, RequiredCommuneId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background refresh of scattered area failed");
            }
        });
    }
}
