using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class FeatureRepository(AppDbContext db) : IFeatureRepository
{
    public async Task<bool> RoadExistsAsync(Guid roadId, Guid userId, CancellationToken ct)
        => await db.Roads.AnyAsync(r => r.Id == roadId && r.UserId == userId, ct);

    public async Task<Guid> SaveFeatureAsync(FeatureBase entity, string featureType, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        FeatureTypeRegistry.AddToDbContext(db, entity);
        db.FeatureRegistry.Add(new FeatureRegistry { Id = entity.Id, FeatureType = featureType });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return entity.Id;
    }

    public async Task<string?> GetFeatureTypeAsync(Guid featureId, CancellationToken ct)
    {
        var reg = await db.FeatureRegistry.FindAsync([featureId], ct);
        return reg?.FeatureType;
    }

    public async Task<bool> OwnsFeatureAsync(Guid featureId, string featureType, Guid userId, CancellationToken ct)
    {
        var dbSet = FeatureTypeRegistry.GetDbSet(db, featureType);
        return dbSet is not null
            && await dbSet.AnyAsync(f => f.Id == featureId && f.UserId == userId, ct);
    }

    public async Task<bool> UpdateFeatureAsync(UpdateFeatureCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var query = command.Descriptor.GetDbSet(db);
        string? dataStr = null;
        if (command.Body.Data is not null)
        {
            dataStr = command.Body.Data.Value.ValueKind == JsonValueKind.String
                ? command.Body.Data.Value.GetString()!
                : command.Body.Data.Value.GetRawText();
        }

        var rows = await query
            .Where(f => f.Id == command.FeatureId && f.UserId == command.UserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.UpdatedAt, command.UpdatedAt)
                .SetProperty(f => f.Label, f => command.Body.Label ?? f.Label)
                .SetProperty(f => f.Data, f => dataStr ?? f.Data)
            , ct);

        if (rows == 0)
        {
            return false;
        }

        if (command.Descriptor.PostUpdateAction is not null)
        {
            await command.Descriptor.PostUpdateAction(db, command.FeatureId, command.UserId, command.Body.Data, ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteFeatureAsync(Guid featureId, Guid userId, string featureType, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var dbSet = FeatureTypeRegistry.GetDbSet(db, featureType);
        if (dbSet is null)
        {
            return false;
        }

        var deleted = await dbSet.Where(f => f.Id == featureId && f.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            return false;
        }

        await db.FeatureRegistry.Where(r => r.Id == featureId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private const int DeleteCommandTimeoutSeconds = 30;

    public async Task<(int total, List<Guid> ids)> ClearAllFeaturesAsync(Guid userId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        var descriptors = FeatureTypeRegistry.GetAllDescriptors();

        var sb = new StringBuilder();
        sb.Append("WITH ");

        for (var i = 0; i < descriptors.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append($"d{i} AS (DELETE FROM {FeatureTypeRegistry.ValidateTableName(descriptors[i].TableName)} WHERE user_id = @uid RETURNING id)");
        }

        sb.AppendLine(",");
        sb.Append("all_deleted AS (");
        for (var i = 0; i < descriptors.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" UNION ALL ");
            }

            sb.Append($"SELECT id FROM d{i}");
        }
        sb.AppendLine("),");
        sb.AppendLine("cleanup AS (DELETE FROM feature_registry WHERE id IN (SELECT id FROM all_deleted) RETURNING id)");
        sb.Append("SELECT id FROM cleanup");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.CommandTimeout = DeleteCommandTimeoutSeconds;
        SqlFragments.AddParam(cmd, "@uid", userId);

        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return (ids.Count, ids);
    }
}
