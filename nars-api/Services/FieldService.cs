using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;

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
                Layer: reader.GetString(2),
                Label: reader.GetString(3),
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
}
