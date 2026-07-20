using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class FieldService(
    AppDbContext db,
    ILogger<FieldService> logger) : IFieldService
{
    public async Task<(List<FieldFeatureResult> Items, int Total)> QueryFeaturesAsync(
        FeatureTypeDescriptor descriptor, int communeId, int skip, int take, CancellationToken ct = default)
    {
        var tableName = FeatureTypeRegistry.ValidateTableName(descriptor.TableName);
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT f.id, f.user_id, f.layer, f.label, f.data, f.created_at, f.updated_at,
                   COUNT(*) OVER() AS total
            FROM {tableName} f
            JOIN users u ON u.id = f.user_id
            WHERE u.commune_id = @commune_id
            ORDER BY f.created_at DESC
            OFFSET @skip
            LIMIT @take
            """;

        SqlFragments.AddParam(cmd, "@commune_id", communeId);
        SqlFragments.AddParam(cmd, "@skip", skip);
        SqlFragments.AddParam(cmd, "@take", take);

        var items = new List<FieldFeatureResult>();
        var total = 0;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var totalOrdinal = reader.GetOrdinal("total");
        while (await reader.ReadAsync(ct))
        {
            if (total == 0)
            {
                total = reader.GetInt32(totalOrdinal);
            }

            var id = reader.GetGuid(0);
            var rawData = await reader.IsDBNullAsync(4, ct) ? "{}" : reader.GetString(4);
            JsonElement? data = null;
            try { data = JsonSerializer.Deserialize<JsonElement>(rawData); }
            catch (JsonException ex) { logger.LogWarning(ex, "Failed to parse feature data for {Id}", id); }

            items.Add(new FieldFeatureResult(
                Id: id.ToString(),
                UserId: reader.GetGuid(1).ToString(),
                Layer: await reader.IsDBNullAsync(2, ct) ? "" : reader.GetString(2),
                Label: await reader.IsDBNullAsync(3, ct) ? "" : reader.GetString(3),
                Data: data,
                CreatedAt: reader.GetDateTime(5),
                UpdatedAt: await reader.IsDBNullAsync(6, ct) ? null : (DateTime?)reader.GetDateTime(6)
            ));
        }

        return (items, total);
    }

    public async Task<(Guid UserId, int? CommuneId)?> GetFeatureOwnerAsync(string featureType, Guid featureId, CancellationToken ct = default)
    {
        var descriptor = FeatureTypeRegistry.GetDescriptor(featureType);
        if (descriptor is null)
        {
            return null;
        }

        var tableName = FeatureTypeRegistry.ValidateTableName(descriptor.TableName);
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT f.user_id, u.commune_id
            FROM {tableName} f
            JOIN users u ON u.id = f.user_id
            WHERE f.id = @id
            """;
        SqlFragments.AddParam(cmd, "@id", featureId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var ownerId = reader.GetGuid(0);
        var communeId = await reader.IsDBNullAsync(1, ct) ? null : (int?)reader.GetInt32(1);
        return (ownerId, communeId);
    }

    public async Task<List<FieldInspectionResponse>> GetInspectionsAsync(Guid featureId, int skip, int take, CancellationToken ct = default)
    {
        return await db.Inspections
            .Where(i => i.FeatureId == featureId)
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(i => new FieldInspectionResponse(
                Id: i.Id.ToString(),
                FeatureId: i.FeatureId.ToString(),
                Type: i.Type,
                Data: JsonHelper.DeserializeSafe(i.Data),
                Status: i.Status,
                CreatedAt: i.CreatedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<(Guid OwnerUserId, int? CommuneId)?> GetRoadOwnerAsync(Guid roadId, CancellationToken ct = default)
    {
        var result = await (
            from r in db.Roads
            join u in db.Users on r.UserId equals u.Id
            where r.Id == roadId
            select new { Road = r, u.CommuneId }
        ).FirstOrDefaultAsync(ct);

        return result is null ? null : (result.Road.UserId, result.CommuneId);
    }

    public async Task<Guid> CreateEntranceAsync(Guid roadId, Guid ownerUserId, Guid creatorUserId, string label, string data, CancellationToken ct = default)
    {
        var newId = Guid.CreateVersion7();

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        db.HouseEntrances.Add(new HouseEntrance
        {
            Id = newId,
            UserId = ownerUserId,
            Layer = FeatureTypes.HouseEntranceLayers.Main,
            Label = label,
            Data = data,
            RoadId = roadId,
        });
        db.FeatureRegistry.Add(new FeatureRegistry
        {
            Id = newId,
            FeatureType = FeatureTypes.HouseEntrance
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return newId;
    }

    public async Task<string?> GetFeatureRegistryTypeAsync(Guid featureId, CancellationToken ct = default)
    {
        var reg = await db.FeatureRegistry.FindAsync([featureId], ct);
        return reg?.FeatureType;
    }

    public async Task<Guid> SubmitInspectionAsync(Guid featureId, Guid userId, string type, string status, string data, CancellationToken ct = default)
    {
        var inspection = new Inspection
        {
            Id = Guid.CreateVersion7(),
            FeatureId = featureId,
            UserId = userId,
            Type = type,
            Data = data,
            Status = status,
        };

        db.Add(inspection);
        await db.SaveChangesAsync(ct);
        return inspection.Id;
    }
}
