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

    public async Task<bool> UpdateFeatureAsync(FeatureTypeDescriptor descriptor, Guid featureId, Guid userId, FeatureUpdateRequest body, DateTime updatedAt, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var query = descriptor.GetDbSet(db);
        string? dataStr = null;
        if (body.Data is not null)
        {
            dataStr = body.Data.Value.ValueKind == JsonValueKind.String
                ? body.Data.Value.GetString()!
                : body.Data.Value.GetRawText();
        }

        var rows = await query
            .Where(f => f.Id == featureId && f.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.UpdatedAt, updatedAt)
                .SetProperty(f => f.Label, f => body.Label ?? f.Label)
                .SetProperty(f => f.Data, f => dataStr ?? f.Data)
            , ct);

        if (rows == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        if (descriptor.PostUpdateAction is not null)
            await descriptor.PostUpdateAction(db, featureId, userId, body.Data, ct);

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteFeatureAsync(Guid featureId, Guid userId, string featureType, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var dbSet = FeatureTypeRegistry.GetDbSet(db, featureType);
        if (dbSet is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        int deleted = await dbSet.Where(f => f.Id == featureId && f.UserId == userId).ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await db.FeatureRegistry.Where(r => r.Id == featureId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<(int total, List<Guid> ids)> ClearAllFeaturesAsync(Guid userId, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        var total = 0;
        var allIds = new List<Guid>();

        // Build a single CTE that deletes from all feature tables and feature_registry
        var sb = new StringBuilder();
        sb.Append("WITH ");

        for (int i = 0; i < descriptors.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"d{i} AS (DELETE FROM {descriptors[i].TableName} WHERE user_id = @uid RETURNING id)");
        }

        sb.AppendLine(",");
        sb.Append("all_deleted AS (");
        for (int i = 0; i < descriptors.Count; i++)
        {
            if (i > 0) sb.Append(" UNION ALL ");
            sb.Append($"SELECT id FROM d{i}");
        }
        sb.AppendLine("),");
        sb.AppendLine("cleanup AS (DELETE FROM feature_registry WHERE id IN (SELECT id FROM all_deleted))");
        sb.Append("SELECT COUNT(*) FROM all_deleted");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.CommandTimeout = 30;
        SqlFragments.AddParam(cmd, "@uid", userId);

        var result = await cmd.ExecuteScalarAsync(ct);
        total = result is long l ? (int)l : 0;

        return (total, allIds);
    }
}
