using System.Data;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Field")]
public class FieldController(
    AppDbContext db,
    ILogger<FieldController> logger,
    IOptions<FeatureDefaultsOptions> featureDefaults,
    IDateTimeProvider timeProvider,
    IFieldService fieldService) : NarsControllerBase
{
    private readonly int _maxFeatureDataSize = featureDefaults.Value.MaxFeatureDataSize;

    /// <summary>Lists features of a given type visible to the field worker's commune.</summary>
    [HttpGet("field/features")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFeatures(
        [FromQuery] string? type = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 500,
        CancellationToken cancellationToken = default)
    {
        if (CurrentUserRole != UserRoles.FieldWorker)
        {
            return Forbid();
        }

        var communeId = CurrentCommuneId;
        if (communeId is null)
        {
            return Problem(detail: "Field worker has no assigned commune.", statusCode: 400);
        }

        if (type is null)
        {
            return Problem(detail: "type query parameter is required.", statusCode: 400);
        }

        var descriptor = FeatureTypeRegistry.GetDescriptor(type.ToLowerInvariant());
        if (descriptor is null)
        {
            return Problem(detail: "Invalid or missing type. Use: road, house_entrance, or naming_panel.", statusCode: 400);
        }

        take = Math.Clamp(take, 1, 1000);
        var (Items, Total) = await fieldService.QueryFeaturesAsync(descriptor, communeId.Value, skip, take, cancellationToken);
        return Ok(new LoadFeaturesResponse<FieldFeatureResult>(Features: Items, Count: Total, Skip: skip, Take: take));
    }

    /// <summary>Submits a field inspection for a feature (road, entrance, or panel).</summary>
    [HttpPost("field/inspect")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitInspection([FromBody] FieldInspectRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (CurrentUserRole != UserRoles.FieldWorker)
        {
            return Forbid();
        }

        if (!Guid.TryParse(body.FeatureId, out var featureId))
        {
            return Problem(detail: "Invalid feature_id.", statusCode: 400);
        }

        var featureError = await ValidateInspectionTargetAsync(featureId, body.Type, cancellationToken);
        if (featureError is not null)
        {
            return featureError;
        }

        var rawData = ExtractJsonData(body.Data);
        if (rawData.Length > _maxFeatureDataSize)
        {
            return Problem(detail: "Inspection data is too large (max 512 KB).", statusCode: 400);
        }

        var statusError = ValidateInspectionStatus(body.Status);
        if (statusError is not null)
        {
            return statusError;
        }

        return await SaveInspectionAsync(featureId, body.Type, body.Status, rawData, cancellationToken);
    }

    /// <summary>Returns all inspections for a given feature, newest first.</summary>
    [HttpGet("field/inspections/{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInspections(Guid featureId, CancellationToken cancellationToken = default)
    {
        if (CurrentUserRole != UserRoles.FieldWorker)
        {
            return Forbid();
        }

        var inspections = await db.Inspections
            .Where(i => i.FeatureId == featureId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new FieldInspectionResponse(
                Id: i.Id.ToString(),
                FeatureId: i.FeatureId.ToString(),
                Type: i.Type,
                Data: DeserializeJsonSafe(i.Data),
                Status: i.Status,
                CreatedAt: i.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Ok(new FieldInspectionsResponse(inspections));
    }

    /// <summary>Creates a house entrance linked to a road from a field inspection.</summary>
    [HttpPost("field/entrance")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEntranceFromInspection([FromBody] FieldEntranceCreateRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (CurrentUserRole != UserRoles.FieldWorker)
        {
            return Forbid();
        }

        if (!Guid.TryParse(body.RoadId, out var roadId))
        {
            return Problem(detail: "Invalid road_id.", statusCode: 400);
        }

        var roadData = await (
            from r in db.Roads
            join u in db.Users on r.UserId equals u.Id
            where r.Id == roadId
            select new { Road = r, u.CommuneId }
        ).FirstOrDefaultAsync(cancellationToken);

        if (roadData is null)
        {
            return Problem(detail: "Road not found.", statusCode: 400);
        }

        if (roadData.CommuneId.HasValue && CurrentCommuneId.HasValue && roadData.CommuneId != CurrentCommuneId)
        {
            return Forbid();
        }

        var rawData = ExtractJsonData(body.Data);

        if (rawData.Length > _maxFeatureDataSize)
        {
            return Problem(detail: "Feature data is too large (max 512 KB).", statusCode: 400);
        }

        var label = body.Label ?? "Entrance (field worker)";
        var newId = Guid.CreateVersion7();

        var entrance = new HouseEntrance
        {
            Id = newId,
            UserId = roadData.Road.UserId,
            Layer = FeatureTypes.HouseEntranceLayers.Main,
            Label = label,
            Data = rawData,
            RoadId = roadId,
            CreatedAt = timeProvider.UtcNow,
        };

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        db.HouseEntrances.Add(entrance);
        db.FeatureRegistry.Add(new FeatureRegistry
        {
            Id = newId,
            FeatureType = FeatureTypes.HouseEntrance
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        logger.LogInformation(
            "[Field] Worker {WorkerId} created entrance {EntranceId} for road {RoadId} (owner: {OwnerId})",
            CurrentUserId, newId, roadId, roadData.Road.UserId);

        return StatusCode(201, new CreateEntranceResponse(
            Success: true,
            Id: newId.ToString(),
            Message: "Entrance created from inspection."
        ));
    }

    private static readonly string[] ValidInspectionTypes = [FeatureTypes.Road, FeatureTypes.HouseEntrance, FeatureTypes.NamingPanel];

    private async Task<IActionResult?> ValidateInspectionTargetAsync(Guid featureId, string type, CancellationToken ct)
    {
        var validTypes = ValidInspectionTypes;
        if (!validTypes.Contains(type))
        {
            return Problem(detail: $"Invalid inspection type. Must be one of: {string.Join(", ", validTypes)}", statusCode: 400);
        }

        var registryEntry = await db.FeatureRegistry.FindAsync([featureId], ct);
        if (registryEntry is null)
        {
            return Problem(detail: "Feature not found.", statusCode: 400);
        }

        var feature = await fieldService.GetFeatureOwnerAsync(registryEntry.FeatureType, featureId, ct);
        if (feature is null)
        {
            return Problem(detail: "Feature not found.", statusCode: 400);
        }

        if (!feature.Value.CommuneId.HasValue || feature.Value.CommuneId != CurrentCommuneId)
        {
            return Forbid();
        }

        return null;
    }

    private static string ExtractJsonData(JsonNode data)
    {
        return data is JsonValue value && value.TryGetValue<string>(out var str)
            ? str
            : data.ToJsonString();
    }

    private IActionResult? ValidateInspectionStatus(string status)
    {
        var validStatuses = new[] { "good", "issue" };
        if (!validStatuses.Contains(status))
        {
            return Problem(detail: "Status must be 'good' or 'issue'.", statusCode: 400);
        }

        return null;
    }

    private async Task<IActionResult> SaveInspectionAsync(Guid featureId, string type, string status, string rawData, CancellationToken ct)
    {
        var inspection = new Inspection
        {
            Id = Guid.CreateVersion7(),
            FeatureId = featureId,
            UserId = RequiredCurrentUserId,
            Type = type,
            Data = rawData,
            Status = status,
            CreatedAt = timeProvider.UtcNow,
        };

        db.Add(inspection);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("[Field] Worker {WorkerId} inspected {Type} {FeatureId} — status: {Status}",
            CurrentUserId, type, featureId, status);

        return StatusCode(201, new FieldInspectSubmitResponse(
            Success: true,
            Id: inspection.Id.ToString(),
            Message: "Inspection saved."
        ));
    }

    private static JsonNode? DeserializeJsonSafe(string json) => JsonHelper.DeserializeSafe(json);
}
