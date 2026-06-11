using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Field")]
public class FieldController(
    AppDbContext db,
    ILogger<FieldController> logger) : NarsControllerBase
{
    private const int MaxFeatureDataSize = 524_288;

    /// <summary>
    /// Returns features available for inspection in the field worker's commune.
    /// Field workers can inspect features created by commune_user accounts
    /// within the same commune.
    /// </summary>
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
        if (user is null) return Unauthorized();
        if (user.Role != UserRoles.FieldWorker)
            return Forbid();

        var communeId = user.CommuneId;
        if (communeId is null)
            return BadRequest(new { detail = "Field worker has no assigned commune." });

        // Find all commune_user IDs in the same commune
        var communeUserIds = await db.Users
            .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId == communeId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (communeUserIds.Count == 0)
            return Ok(new { features = Array.Empty<object>(), count = 0 });

        take = Math.Clamp(take, 1, 1000);
        var userIds = communeUserIds.ToArray();

        // Query the specific feature type table if type is specified
        (List<object> Items, int Total)? results = (type?.ToLowerInvariant()) switch
        {
            FeatureTypes.Road => await QueryFeaturesAsync("roads", userIds, skip, take),
            FeatureTypes.HouseEntrance => await QueryFeaturesAsync("house_entrances", userIds, skip, take),
            FeatureTypes.NamingPanel => await QueryFeaturesAsync("naming_panels", userIds, skip, take),
            _ => null
        };

        if (results is null)
            return BadRequest(new { detail = "Invalid or missing type. Use: road, house_entrance, or naming_panel." });

        return Ok(new { features = results.Value.Items, count = results.Value.Total });
    }

    /// <summary>
    /// Saves a new inspection result for a feature.
    /// Field workers submit road/entrance/naming panel inspection data here.
    /// </summary>
    [HttpPost("field/inspect")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitInspection([FromBody] FieldInspectRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new { detail = "Request body is required." });
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null || user.Role != UserRoles.FieldWorker)
            return Forbid();

        if (!Guid.TryParse(body.FeatureId, out var featureId))
            return BadRequest(new { detail = "Invalid feature_id." });

        // Verify the feature exists and belongs to a user in the same commune
        var registryEntry = await db.FeatureRegistry.FindAsync([featureId], cancellationToken);
        if (registryEntry is null)
            return BadRequest(new { detail = "Feature not found." });

        var feature = await GetFeatureOwnerAsync(registryEntry.FeatureType, featureId);
        if (feature is null)
            return BadRequest(new { detail = "Feature not found." });

        if (feature.Value.CommuneId != user.CommuneId)
            return Forbid();

        var validTypes = new[] { "road", "house_entrance", "naming_panel" };
        if (!validTypes.Contains(body.Type))
            return BadRequest(new { detail = $"Invalid inspection type. Must be one of: {string.Join(", ", validTypes)}" });

        var rawData = body.Data.ValueKind == JsonValueKind.String
            ? body.Data.GetString()!
            : body.Data.GetRawText();

        if (rawData.Length > MaxFeatureDataSize)
            return BadRequest(new { detail = "Inspection data is too large (max 512 KB)." });

        var validStatuses = new[] { "good", "issue" };
        if (!validStatuses.Contains(body.Status))
            return BadRequest(new { detail = "Status must be 'good' or 'issue'." });

        var inspection = new Inspection
        {
            Id = Guid.CreateVersion7(),
            FeatureId = featureId,
            UserId = RequiredCurrentUserId,
            Type = body.Type,
            Data = rawData,
            Status = body.Status,
            CreatedAt = DateTime.UtcNow,
        };

        db.Add(inspection);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[Field] Worker {WorkerId} inspected {Type} {FeatureId} — status: {Status}",
            CurrentUserId, body.Type, featureId, body.Status);

        return StatusCode(201, new
        {
            success = true,
            id = inspection.Id.ToString(),
            message = "Inspection saved."
        });
    }

    /// <summary>
    /// Gets the inspection history for a specific feature.
    /// </summary>
    [HttpGet("field/inspections/{featureId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInspections(Guid featureId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null || user.Role != UserRoles.FieldWorker)
            return Forbid();

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

        return Ok(new { inspections });
    }

    /// <summary>
    /// Creates a new house entrance feature from the inspection form.
    /// Used when a field worker finds a missing entrance and needs to add one.
    /// </summary>
    [HttpPost("field/entrance/create")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEntranceFromInspection([FromBody] FieldEntranceCreateRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new { detail = "Request body is required." });
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null || user.Role != UserRoles.FieldWorker)
            return Forbid();

        if (!Guid.TryParse(body.RoadId, out var roadId))
            return BadRequest(new { detail = "Invalid road_id." });

        // Verify the road exists and belongs to a user in the same commune
        var road = await db.Roads.FindAsync([roadId], cancellationToken);
        if (road is null)
            return BadRequest(new { detail = "Road not found." });

        var roadOwner = await db.Users.FindAsync([road.UserId], cancellationToken);
        if (roadOwner is null || roadOwner.CommuneId != user.CommuneId)
            return Forbid();

        var rawData = body.Data.ValueKind == JsonValueKind.String
            ? body.Data.GetString()!
            : body.Data.GetRawText();

        if (rawData.Length > MaxFeatureDataSize)
            return BadRequest(new { detail = "Feature data is too large (max 512 KB)." });

        var label = body.Label ?? "Entrance (field worker)";
        var newId = Guid.CreateVersion7();

        var entrance = new HouseEntrance
        {
            Id = newId,
            UserId = road.UserId, // entrance belongs to the road owner's commune_user
            Layer = FeatureTypes.HouseEntranceLayers.Main,
            Label = label,
            Data = rawData,
            RoadId = roadId,
            CreatedAt = DateTime.UtcNow,
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

        logger.LogInformation(
            "[Field] Worker {WorkerId} created entrance {EntranceId} for road {RoadId} (owner: {OwnerId})",
            CurrentUserId, newId, roadId, road.UserId);

        return StatusCode(201, new
        {
            success = true,
            id = newId.ToString(),
            message = "Entrance created from inspection."
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonElement DeserializeJsonSafe(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement; }
    }

    private async Task<(List<object> Items, int Total)> QueryFeaturesAsync(
        string tableName, Guid[] userIds, int skip, int take)
    {
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT id, user_id, layer, label, data, created_at, updated_at,
                       COUNT(*) OVER() AS total
                FROM {tableName}
                WHERE user_id = ANY(@user_ids)
                ORDER BY created_at DESC
                OFFSET @skip
                LIMIT @take
                """;

            SqlFragments.AddParam(cmd, "@user_ids", userIds);
            SqlFragments.AddParam(cmd, "@skip", skip);
            SqlFragments.AddParam(cmd, "@take", take);

            var items = new List<object>();
            int total = 0;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (total == 0) total = reader.GetInt32(6);
                var id = reader.GetGuid(0);
                var rawData = await reader.IsDBNullAsync(4) ? "{}" : reader.GetString(4);
                JsonElement? data = null;
                try { data = JsonSerializer.Deserialize<JsonElement>(rawData); }
                catch (JsonException ex) { logger.LogWarning(ex, "Failed to parse feature data for {Id}", id); }

                items.Add(new
                {
                    id = id.ToString(),
                    user_id = reader.GetGuid(1).ToString(),
                    layer = reader.GetString(2),
                    label = reader.GetString(3),
                    data,
                    created_at = reader.GetDateTime(5),
                    updated_at = await reader.IsDBNullAsync(6) ? null : (DateTime?)reader.GetDateTime(6)
                });
            }

            return (items, total);
        }
        finally
        {
            if (!wasOpen && conn.State == System.Data.ConnectionState.Open)
                await conn.CloseAsync();
        }
    }

    private async Task<(Guid UserId, int? CommuneId)?> GetFeatureOwnerAsync(string featureType, Guid featureId)
    {
        var tableName = FeatureTypeRegistry.GetDescriptor(featureType)?.TableName;
        if (tableName is null) return null;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT user_id FROM {tableName} WHERE id = @id";
            SqlFragments.AddParam(cmd, "@id", featureId);

            var result = await cmd.ExecuteScalarAsync();
            if (result is null || result == DBNull.Value) return null;

            var userId = (Guid)result;
            var owner = await db.Users.FindAsync([userId], CancellationToken.None);
            return owner is null ? null : (owner.Id, owner.CommuneId);
        }
        finally
        {
            if (!wasOpen && conn.State == System.Data.ConnectionState.Open)
                await conn.CloseAsync();
        }
    }
}
