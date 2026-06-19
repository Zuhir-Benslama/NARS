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

[ApiController]
[Route("/api")]
[Tags("Features")]
public class FeaturesController(
    AppDbContext db,
    IScatteredAreaService scatteredService,
    IBackgroundTaskQueue bgQueue,
    ILogger<FeaturesController> logger,
    IConfiguration config,
    IDateTimeProvider timeProvider,
    IFeatureStatsService featureStatsService) : NarsControllerBase
{
    private int MaxFeatureDataSize => int.TryParse(config["FeatureDefaults:MaxFeatureDataSize"], out var v) ? v : 524_288;

    [HttpPost("save")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SaveFeature([FromBody] FeatureSaveRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new { detail = "Request body is required." });
        if (!FeatureTypes.AllTypes.Contains(body.Type))
            return BadRequest(new { detail = $"Unknown feature type '{body.Type}'." });
        if (!FeatureTypes.IsValidLayer(body.Type, body.Layer))
            return BadRequest(new { detail = $"Layer '{body.Layer}' is not valid for type '{body.Type}'." });

        if (body.Type == FeatureTypes.Area && body.Layer == FeatureTypes.AreaLayers.Scattered)
            return BadRequest(new { detail = "Scattered areas are auto-computed and cannot be saved manually." });

        var rawJson = body.Data.GetRawText();
        if (rawJson.Length > MaxFeatureDataSize)
            return BadRequest(new { detail = "Feature data is too large (max 512 KB)." });

        var dataJson = rawJson;

        Guid? roadId = null;
        if (body.Type == FeatureTypes.HouseEntrance && body.Layer == FeatureTypes.HouseEntranceLayers.Main
            && body.Data.TryGetProperty("roadDbId", out var ridEl) && ridEl.ValueKind == JsonValueKind.String && Guid.TryParse(ridEl.GetString(), out var rid))
            roadId = rid;

        if (roadId.HasValue && !await db.Roads.AnyAsync(r => r.Id == roadId.Value && r.UserId == CurrentUserId, cancellationToken))
            return BadRequest(new { detail = "Referenced road not found." });

        Guid newId = Guid.CreateVersion7();

        var entity = FeatureTypeRegistry.CreateEntity(body.Type, newId, RequiredCurrentUserId, body.Layer, body.Label, dataJson);

        if (entity is HouseEntrance entrance)
            entrance.RoadId = roadId;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        FeatureTypeRegistry.AddToDbContext(db, entity!);

        db.FeatureRegistry.Add(new FeatureRegistry { Id = newId, FeatureType = body.Type });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (body.Type == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return StatusCode(201, new SaveFeatureResponse(Success: true, Id: newId.ToString(), Message: "Feature saved successfully"));
    }

    [HttpGet("load")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoadFeatures([FromQuery] int skip = 0, [FromQuery] int take = 1000, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 2000);

        var (features, totalCount) = await featureStatsService.LoadAllFeaturesAsync(
            RequiredCurrentUserId, skip, take, cancellationToken);

        return Ok(new LoadFeaturesResponse(
            Features: features,
            Count: totalCount,
            Skip: skip,
            Take: take
        ));
    }

    [HttpPost("clear")]
    [EnableRateLimiting(RateLimitPolicies.Clear)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearFeatures([FromBody] ClearFeaturesRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new { detail = "Request body is required." });
        if (!body.Confirm)
            return BadRequest(new { detail = "Set \"confirm\": true to delete all features." });

        var uid = CurrentUserId;
        int total = 0;
        var userFeatureIds = new List<Guid>();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(cancellationToken);

        foreach (var type in FeatureTypeRegistry.GetAllTypes())
        {
            var descriptor = FeatureTypeRegistry.GetDescriptor(type);
            if (descriptor?.TableName is null) continue;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {descriptor.TableName} WHERE user_id = @uid RETURNING id";
            var uidParam = cmd.CreateParameter();
            uidParam.ParameterName = "@uid";
            uidParam.Value = uid;
            cmd.Parameters.Add(uidParam);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                userFeatureIds.Add(reader.GetGuid(0));
                total++;
            }
        }

        if (userFeatureIds.Count > 0)
        {
            await db.FeatureRegistry
                .Where(r => userFeatureIds.Contains(r.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return Ok(new ActionResponse(Success: true, Message: $"Deleted {total} features"));
    }

    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken = default)
    {
        var counts = await featureStatsService.GetFeatureCountsAsync(RequiredCurrentUserId, cancellationToken);

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

    [HttpPut("update/{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFeature(Guid featureId, [FromBody] FeatureUpdateRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new { detail = "Request body is required." });
        var reg = await db.FeatureRegistry.FindAsync([featureId], cancellationToken);
        if (reg is null) return NotFound(new { detail = "Feature not found" });

        var owned = await FeatureTypeRegistry.GetDbSet(db, reg.FeatureType)!
            .AnyAsync(f => f.Id == featureId && f.UserId == CurrentUserId, cancellationToken);
        if (!owned) return NotFound(new { detail = "Feature not found" });

        if (body.Data is JsonElement dataElement)
        {
            var rawJson = dataElement.GetRawText();
            if (rawJson.Length > MaxFeatureDataSize)
                return BadRequest(new { detail = "Feature data is too large (max 512 KB)." });
        }

        var descriptor = FeatureTypeRegistry.GetDescriptor(reg.FeatureType);
        if (descriptor is null)
            return BadRequest(new { detail = $"Unknown feature type in registry: {reg.FeatureType}" });

        var updatedAt = timeProvider.UtcNow;
        int updated = await UpdateEntity(descriptor, featureId, body, updatedAt, cancellationToken);
        if (updated == 0) return NotFound(new { detail = "Feature not found" });

        if (reg.FeatureType == FeatureTypes.Area)
            await QueueScatteredRefresh();

        return Ok(new UpdateFeatureResponse(Success: true, Id: featureId.ToString(), UpdatedAt: updatedAt));
    }

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

    private async Task<int> UpdateEntity(FeatureTypeDescriptor descriptor, Guid id, FeatureUpdateRequest body, DateTime updatedAt, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var query = descriptor.GetDbSet(db);
        string? dataStr = null;
        if (body.Data is not null)
        {
            dataStr = body.Data.Value.ValueKind == JsonValueKind.String
                ? body.Data.Value.GetString()!
                : body.Data.Value.GetRawText();
        }

        var rows = await query
            .Where(f => f.Id == id && f.UserId == CurrentUserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.UpdatedAt, updatedAt)
                .SetProperty(f => f.Label, f => body.Label ?? f.Label)
                .SetProperty(f => f.Data, f => dataStr ?? f.Data)
            , cancellationToken);

        if (rows == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return 0;
        }

        if (descriptor.PostUpdateAction is not null)
            await descriptor.PostUpdateAction(db, id, RequiredCurrentUserId, body.Data, cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return rows;
    }

    private async ValueTask QueueScatteredRefresh()
    {
        var currentUserId = RequiredCurrentUserId;
        var currentCommuneId = RequiredCommuneId;
        await bgQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
        {
            try
            {
                var svc = sp.GetRequiredService<IScatteredAreaService>();
                await svc.RefreshAsync(currentUserId, currentCommuneId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background refresh of scattered area failed");
            }
        });
    }
}
