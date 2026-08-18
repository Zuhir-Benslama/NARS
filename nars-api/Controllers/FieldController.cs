using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Field")]
[Authorize(Roles = UserRoles.FieldWorker)]
public class FieldController(
    ILogger<FieldController> logger,
    IOptions<FeatureDefaultsOptions> featureDefaults,
    IFieldService fieldService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    private readonly int _maxFeatureDataSize = featureDefaults.Value.MaxFeatureDataSize;
    private const string DefaultEntranceLabel = "Entrance (field worker)";

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

        (skip, take) = Pagination.Clamp(skip, take);
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
        if (!Guid.TryParse(body.FeatureId, out var featureId))
        {
            return Problem(detail: "Invalid feature_id.", statusCode: 400);
        }

        var normalizedType = body.Type.ToLowerInvariant();
        var featureError = await ValidateInspectionTargetAsync(featureId, normalizedType, cancellationToken);
        if (featureError is not null)
        {
            return featureError;
        }

        var rawData = ExtractJsonData(body.Data);
        if (rawData.Length > _maxFeatureDataSize)
        {
            return Problem(detail: $"Inspection data is too large (max {_maxFeatureDataSize / 1024} KB).", statusCode: 400);
        }

        var statusError = ValidateInspectionStatus(body.Status);
        if (statusError is not null)
        {
            return statusError;
        }

        var inspectionId = await fieldService.SubmitInspectionAsync(
            featureId, RequiredCurrentUserId, normalizedType, body.Status, rawData, cancellationToken);

        logger.LogInformation("[Field] Worker {WorkerId} inspected {Type} {FeatureId} — status: {Status}",
            CurrentUserId, body.Type.ReplaceLineEndings(" "), featureId, body.Status.ReplaceLineEndings(" "));

        return StatusCode(201, new CreateResponse(
            Success: true,
            Id: inspectionId.ToString(),
            Message: "Inspection saved."
        ));
    }

    /// <summary>Returns inspections for a given feature, newest first, with pagination.</summary>
    [HttpGet("field/inspections/{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInspections(
        Guid featureId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        (skip, take) = Pagination.Clamp(skip, take);

        var communeId = CurrentCommuneId;
        if (communeId is null)
        {
            return Problem(detail: "Field worker has no assigned commune.", statusCode: 400);
        }

        var registryType = await fieldService.GetFeatureRegistryTypeAsync(featureId, cancellationToken);
        if (registryType is null)
        {
            return Problem(detail: "Feature not found.", statusCode: 404);
        }

        var owner = await fieldService.GetFeatureOwnerAsync(registryType, featureId, cancellationToken);
        if (owner is null || owner.Value.CommuneId != communeId)
        {
            return Forbid();
        }

        var inspections = await fieldService.GetInspectionsAsync(featureId, skip, take, cancellationToken);
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

        if (!Guid.TryParse(body.RoadId, out var roadId))
        {
            return Problem(detail: "Invalid road_id.", statusCode: 400);
        }

        var roadOwner = await fieldService.GetRoadOwnerAsync(roadId, cancellationToken);
        if (roadOwner is null)
        {
            return Problem(detail: "Road not found.", statusCode: 400);
        }

        if (!roadOwner.Value.CommuneId.HasValue || !CurrentCommuneId.HasValue || roadOwner.Value.CommuneId != CurrentCommuneId)
        {
            return Forbid();
        }

        var rawData = ExtractJsonData(body.Data);

        if (rawData.Length > _maxFeatureDataSize)
        {
            return Problem(detail: $"Feature data is too large (max {_maxFeatureDataSize / 1024} KB).", statusCode: 400);
        }

        var label = body.Label ?? DefaultEntranceLabel;
        var newId = await fieldService.CreateEntranceAsync(
            roadId, roadOwner.Value.OwnerUserId, RequiredCurrentUserId, label, rawData, cancellationToken);

        logger.LogInformation(
            "[Field] Worker {WorkerId} created entrance {EntranceId} for road {RoadId} (owner: {OwnerId})",
            CurrentUserId, newId, roadId, roadOwner.Value.OwnerUserId);

        return StatusCode(201, new CreateResponse(
            Success: true,
            Id: newId.ToString(),
            Message: "Entrance created from inspection."
        ));
    }

    private async Task<IActionResult?> ValidateInspectionTargetAsync(Guid featureId, string type, CancellationToken ct)
    {
        var validTypes = FieldService.ValidInspectionTypes;
        if (!validTypes.Contains(type))
        {
            return Problem(detail: $"Invalid inspection type. Must be one of: {string.Join(", ", validTypes)}", statusCode: 400);
        }

        var registryType = await fieldService.GetFeatureRegistryTypeAsync(featureId, ct);
        if (registryType is null)
        {
            return Problem(detail: "Feature not found.", statusCode: 400);
        }

        // The submitted inspection type must match the feature's registered type.
        if (!string.Equals(registryType, type, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(detail: $"Inspection type '{type}' does not match the feature's type '{registryType}'.", statusCode: 400);
        }

        var feature = await fieldService.GetFeatureOwnerAsync(registryType, featureId, ct);
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

    private static string ExtractJsonData(JsonNode data) => data is JsonValue value && value.TryGetValue<string>(out var str)
            ? str
            : data.ToJsonString();

    private ObjectResult? ValidateInspectionStatus(string status)
    {
        if (!FieldService.ValidInspectionStatuses.Contains(status))
        {
            return Problem(detail: "Status must be 'good' or 'issue'.", statusCode: 400);
        }

        return null;
    }
}
