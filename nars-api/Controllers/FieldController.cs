using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    IConfiguration config,
    IDateTimeProvider timeProvider,
    IFieldService fieldService) : NarsControllerBase
{
    private int MaxFeatureDataSize => int.TryParse(config["FeatureDefaults:MaxFeatureDataSize"], out var v) ? v : 524_288;

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
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (user.Role != UserRoles.FieldWorker)
        {
            return Forbid();
        }

        var communeId = user.CommuneId;
        if (communeId is null)
        {
            return Problem(detail: "Field worker has no assigned commune.", statusCode: 400);
        }

        var communeUserIds = await db.Users
            .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId == communeId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (communeUserIds.Count == 0)
        {
            return Ok(new LoadFeaturesResponse(Features: Array.Empty<object>(), Count: 0, Skip: 0, Take: 0));
        }

        take = Math.Clamp(take, 1, 1000);
        var userIds = communeUserIds.ToArray();

        if (type is null)
        {
            return Problem(detail: "type query parameter is required.", statusCode: 400);
        }

        var descriptor = FeatureTypeRegistry.GetDescriptor(type.ToLowerInvariant());
        if (descriptor is null)
        {
            return Problem(detail: "Invalid or missing type. Use: road, house_entrance, or naming_panel.", statusCode: 400);
        }

        var (Items, Total) = await fieldService.QueryFeaturesAsync(descriptor, userIds, skip, take, cancellationToken);
        return Ok(new LoadFeaturesResponse(Features: Items, Count: Total, Skip: skip, Take: take));
    }

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

        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null || user.Role != UserRoles.FieldWorker)
        {
            return Forbid();
        }

        if (!Guid.TryParse(body.FeatureId, out var featureId))
        {
            return Problem(detail: "Invalid feature_id.", statusCode: 400);
        }

        var registryEntry = await db.FeatureRegistry.FindAsync([featureId], cancellationToken);
        if (registryEntry is null)
        {
            return Problem(detail: "Feature not found.", statusCode: 400);
        }

        var feature = await fieldService.GetFeatureOwnerAsync(registryEntry.FeatureType, featureId, cancellationToken);
        if (feature is null)
        {
            return Problem(detail: "Feature not found.", statusCode: 400);
        }

        if (feature.Value.CommuneId != user.CommuneId)
        {
            return Forbid();
        }

        var validTypes = new[] { FeatureTypes.Road, FeatureTypes.HouseEntrance, FeatureTypes.NamingPanel };
        if (!validTypes.Contains(body.Type))
        {
            return Problem(detail: $"Invalid inspection type. Must be one of: {string.Join(", ", validTypes)}", statusCode: 400);
        }

        var rawData = body.Data.ValueKind == JsonValueKind.String
            ? body.Data.GetString()!
            : body.Data.GetRawText();

        if (rawData.Length > MaxFeatureDataSize)
        {
            return Problem(detail: "Inspection data is too large (max 512 KB).", statusCode: 400);
        }

        var validStatuses = new[] { "good", "issue" };
        if (!validStatuses.Contains(body.Status))
        {
            return Problem(detail: "Status must be 'good' or 'issue'.", statusCode: 400);
        }

        var inspection = new Inspection
        {
            Id = Guid.CreateVersion7(),
            FeatureId = featureId,
            UserId = RequiredCurrentUserId,
            Type = body.Type,
            Data = rawData,
            Status = body.Status,
            CreatedAt = timeProvider.UtcNow,
        };

        db.Add(inspection);
        await db.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("[Field] Worker {WorkerId} inspected {Type} {FeatureId} — status: {Status}",
                CurrentUserId, body.Type, featureId, body.Status);
        }

        return StatusCode(201, new FieldInspectSubmitResponse(
            Success: true,
            Id: inspection.Id.ToString(),
            Message: "Inspection saved."
        ));
    }

    [HttpGet("field/inspections/{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInspections(Guid featureId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null || user.Role != UserRoles.FieldWorker)
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

    [HttpPost("field/entrance/create")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEntranceFromInspection([FromBody] FieldEntranceCreateRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null || user.Role != UserRoles.FieldWorker)
        {
            return Forbid();
        }

        if (!Guid.TryParse(body.RoadId, out var roadId))
        {
            return Problem(detail: "Invalid road_id.", statusCode: 400);
        }

        var road = await db.Roads.FindAsync([roadId], cancellationToken);
        if (road is null)
        {
            return Problem(detail: "Road not found.", statusCode: 400);
        }

        var roadOwner = await db.Users.FindAsync([road.UserId], cancellationToken);
        if (roadOwner is null)
        {
            return Forbid();
        }

        if (roadOwner.LockedUntil.HasValue && roadOwner.LockedUntil > timeProvider.UtcNow)
        {
            return Forbid();
        }

        if (roadOwner.CommuneId.HasValue && user.CommuneId.HasValue && roadOwner.CommuneId != user.CommuneId)
        {
            return Forbid();
        }

        var rawData = body.Data.ValueKind == JsonValueKind.String
            ? body.Data.GetString()!
            : body.Data.GetRawText();

        if (rawData.Length > MaxFeatureDataSize)
        {
            return Problem(detail: "Feature data is too large (max 512 KB).", statusCode: 400);
        }

        var label = body.Label ?? "Entrance (field worker)";
        var newId = Guid.CreateVersion7();

        var entrance = new HouseEntrance
        {
            Id = newId,
            UserId = road.UserId,
            Layer = FeatureTypes.HouseEntranceLayers.Main,
            Label = label,
            Data = rawData,
            RoadId = roadId,
            CreatedAt = timeProvider.UtcNow,
        };

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        db.HouseEntrances.Add(entrance);
        await db.SaveChangesAsync(cancellationToken);

        db.FeatureRegistry.Add(new FeatureRegistry
        {
            Id = newId,
            FeatureType = FeatureTypes.HouseEntrance
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "[Field] Worker {WorkerId} created entrance {EntranceId} for road {RoadId} (owner: {OwnerId})",
                CurrentUserId, newId, roadId, road.UserId);
        }

        return StatusCode(201, new CreateEntranceResponse(
            Success: true,
            Id: newId.ToString(),
            Message: "Entrance created from inspection."
        ));
    }

    private static JsonElement DeserializeJsonSafe(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement; }
    }
}
