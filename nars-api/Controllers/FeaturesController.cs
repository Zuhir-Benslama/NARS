using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api/features")]
[Tags("Features")]
public class FeaturesController(
    IFeatureRepository featureRepo,
    IBackgroundTaskQueue bgQueue,
    ILogger<FeaturesController> logger,
    IOptions<FeatureDefaultsOptions> featureDefaults,
    IDateTimeProvider timeProvider,
    IFeatureStatsService featureStatsService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    private readonly int _maxFeatureDataSize = featureDefaults.Value.MaxFeatureDataSize;

    /// <summary>Creates a new geographic feature (road, area, district, building, etc.).</summary>
    [HttpPost("")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SaveFeature([FromBody] FeatureSaveRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (!FeatureTypes.AllTypes.Contains(body.Type))
        {
            return Problem(detail: $"Unknown feature type '{body.Type}'.", statusCode: 400);
        }

        if (!FeatureTypes.IsValidLayer(body.Type, body.Layer))
        {
            return Problem(detail: $"Layer '{body.Layer}' is not valid for type '{body.Type}'.", statusCode: 400);
        }

        if (body.Type == FeatureTypes.Area && body.Layer == FeatureTypes.AreaLayers.Scattered)
        {
            return Problem(detail: "Scattered areas are auto-computed and cannot be saved manually.", statusCode: 400);
        }

        var dataJson = body.Data.GetRawText();
        if (dataJson.Length > _maxFeatureDataSize)
        {
            return Problem(detail: "Feature data is too large (max 512 KB).", statusCode: 400);
        }

        var roadId = TryExtractRoadId(body);

        if (roadId.HasValue && !await featureRepo.RoadExistsAsync(roadId.Value, RequiredCurrentUserId, cancellationToken))
        {
            return Problem(detail: "Referenced road not found.", statusCode: 400);
        }

        var newId = Guid.CreateVersion7();

        var entity = FeatureTypeRegistry.CreateEntity(body.Type, newId, RequiredCurrentUserId, body.Layer, body.Label, dataJson, timeProvider.UtcNow)!;

        if (entity is HouseEntrance entrance)
        {
            entrance.RoadId = roadId;
        }

        await featureRepo.SaveFeatureAsync(entity, body.Type, cancellationToken);

        if (body.Type == FeatureTypes.Area)
        {
            await QueueScatteredRefresh();
        }

        return StatusCode(201, new SaveFeatureResponse(Success: true, Id: newId.ToString(), Message: "Feature saved successfully"));
    }

    /// <summary>Loads the authenticated user's features with pagination.</summary>
    [HttpGet("")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoadFeatures([FromQuery] int skip = 0, [FromQuery] int take = 1000, CancellationToken cancellationToken = default)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 2000);

        var (features, totalCount) = await featureStatsService.LoadAllFeaturesAsync(
            RequiredCurrentUserId, skip, take, cancellationToken);

        return Ok(new LoadFeaturesResponse<FeatureResult>(
            Features: features,
            Count: totalCount,
            Skip: skip,
            Take: take
        ));
    }

    /// <summary>Deletes all features owned by the authenticated user.</summary>
    [HttpPost("clear")]
    [EnableRateLimiting(RateLimitPolicies.Clear)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ClearFeatures([FromBody] ClearFeaturesRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (!body.Confirm)
        {
            return Problem(detail: "Set \"confirm\": true to delete all features.", statusCode: 400);
        }

        var (total, _) = await featureRepo.ClearAllFeaturesAsync(RequiredCurrentUserId, cancellationToken);

        return Ok(ApiResponse.Ok($"Deleted {total} features"));
    }

    /// <summary>Returns feature count breakdown by type for the authenticated user.</summary>
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

    /// <summary>Updates an existing feature's label and/or data payload.</summary>
    [HttpPut("{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFeature(Guid featureId, [FromBody] FeatureUpdateRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var featureType = await featureRepo.GetFeatureTypeAsync(featureId, cancellationToken);
        if (featureType is null)
        {
            return Problem(detail: "Feature not found", statusCode: 404);
        }

        if (!await featureRepo.OwnsFeatureAsync(featureId, featureType, RequiredCurrentUserId, cancellationToken))
        {
            return Problem(detail: "Feature not found", statusCode: 404);
        }

        if (body.Data is JsonElement dataElement)
        {
            var rawJson = dataElement.GetRawText();
            if (rawJson.Length > _maxFeatureDataSize)
            {
                return Problem(detail: "Feature data is too large (max 512 KB).", statusCode: 400);
            }
        }

        var descriptor = FeatureTypeRegistry.GetDescriptor(featureType);
        if (descriptor is null)
        {
            return Problem(detail: $"Unknown feature type in registry: {featureType}", statusCode: 400);
        }

        var updatedAt = timeProvider.UtcNow;
        var command = new UpdateFeatureCommand(descriptor, featureId, RequiredCurrentUserId, body, updatedAt);
        if (!await featureRepo.UpdateFeatureAsync(command, cancellationToken))
        {
            return Problem(detail: "Feature not found", statusCode: 404);
        }

        if (featureType == FeatureTypes.Area)
        {
            await QueueScatteredRefresh();
        }

        return Ok(new UpdateFeatureResponse(Success: true, Id: featureId.ToString(), UpdatedAt: updatedAt));
    }

    /// <summary>Deletes a single feature owned by the authenticated user.</summary>
    [HttpDelete("{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFeature(Guid featureId, CancellationToken cancellationToken = default)
    {
        var featureType = await featureRepo.GetFeatureTypeAsync(featureId, cancellationToken);
        if (featureType is null)
        {
            return Problem(detail: "Feature not found", statusCode: 404);
        }

        if (!await featureRepo.DeleteFeatureAsync(featureId, RequiredCurrentUserId, featureType, cancellationToken))
        {
            return Problem(detail: "Feature not found", statusCode: 404);
        }

        if (featureType == FeatureTypes.Area)
        {
            await QueueScatteredRefresh();
        }

        return Ok(ApiResponse.Ok("Feature deleted successfully"));
    }

    private static Guid? TryExtractRoadId(FeatureSaveRequest body)
    {
        if (body.Type != FeatureTypes.HouseEntrance || body.Layer != FeatureTypes.HouseEntranceLayers.Main)
        {
            return null;
        }

        if (!body.Data.TryGetProperty("roadDbId", out var ridEl) || ridEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Guid.TryParse(ridEl.GetString(), out var rid) ? rid : null;
    }

    private async ValueTask QueueScatteredRefresh()
    {
        var currentUserId = RequiredCurrentUserId;
        var currentCommuneId = CurrentCommuneId;
        if (currentCommuneId is null)
        {
            return;
        }

        await bgQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
        {
            try
            {
                var svc = sp.GetRequiredService<IScatteredAreaService>();
                await svc.RefreshAsync(currentUserId, currentCommuneId.Value, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background refresh of scattered area failed");
            }
        });
    }
}
