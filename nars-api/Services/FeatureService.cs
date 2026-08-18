using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class FeatureService(
    AppDbContext db,
    IBackgroundTaskQueue bgQueue,
    IFeatureCleanupService cleanupService,
    ILogger<FeatureService> logger) : IFeatureService
{
    public async Task<bool> RoadExistsAsync(Guid roadId, Guid userId, CancellationToken ct)
        => await db.Roads.AnyAsync(r => r.Id == roadId && r.UserId == userId, ct);

    public async Task<Guid> SaveFeatureAsync(FeatureBase entity, string featureType, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

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
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var query = command.Descriptor.GetDbSet(db);
        string? dataStr = null;
        if (command.Body.Data is not null)
        {
            dataStr = command.Body.Data.Value.ValueKind == JsonValueKind.String
                ? command.Body.Data.Value.GetString()!
                : command.Body.Data.Value.GetRawText();
        }

        var updatedAt = command.UpdatedAt;
        var newLabel = command.Body.Label;
        var rows = await query
            .Where(f => f.Id == command.FeatureId && f.UserId == command.UserId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.UpdatedAt, updatedAt)
                .SetProperty(f => f.Label, f => newLabel ?? f.Label)
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
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

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

        // Roads own their entrances: the schema has no FK/cascade, so the
        // delete contract is enforced here. A deleted road must not leave
        // orphaned house_entrances rows pointing at a non-existent road.
        if (featureType == FeatureTypes.Road)
        {
            var orphanedEntranceIds = db.HouseEntrances
                .Where(e => e.RoadId == featureId)
                .Select(e => e.Id);
            await db.FeatureRegistry
                .Where(r => orphanedEntranceIds.Contains(r.Id))
                .ExecuteDeleteAsync(ct);
            await db.HouseEntrances
                .Where(e => e.RoadId == featureId)
                .ExecuteDeleteAsync(ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> ClearAllFeaturesAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var total = await cleanupService.DeleteAllFeaturesForUserAsync(db, userId, ct);

        await tx.CommitAsync(ct);
        return total;
    }

    public async ValueTask QueueScatteredRefreshAsync(Guid userId, int? communeId)
    {
        if (communeId is null)
        {
            return;
        }

        await bgQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
        {
            try
            {
                var svc = sp.GetRequiredService<IScatteredAreaService>();
                await svc.RefreshAsync(userId, communeId.Value, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background refresh of scattered area failed");
            }
        });
    }
}
