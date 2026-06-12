using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

public class FieldService(
    AppDbContext db,
    ILogger<FieldService> logger) : IFieldService
{
    public async Task<(List<object> Items, int Total)> QueryFeaturesAsync(
        string tableName, Guid[] userIds, int skip, int take, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

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

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
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

    public async Task<(Guid UserId, int? CommuneId)?> GetFeatureOwnerAsync(string featureType, Guid featureId, CancellationToken ct = default)
    {
        var tableName = FeatureTypeRegistry.GetDescriptor(featureType)?.TableName;
        if (tableName is null) return null;

        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT user_id FROM {tableName} WHERE id = @id";
        SqlFragments.AddParam(cmd, "@id", featureId);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result == DBNull.Value) return null;

        var userId = (Guid)result;
        var owner = await db.Users.FindAsync([userId], ct);
        return owner is null ? null : (owner.Id, owner.CommuneId);
    }
}
