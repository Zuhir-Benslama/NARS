using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class FeatureService(
    IDbContextFactory<AppDbContext> dbFactory,
    IBackgroundTaskQueue bgQueue,
    IFeatureCleanupService cleanupService,
    ILogger<FeatureService> logger) : IFeatureService
{
    public async Task<bool> RoadExistsAsync(Guid roadId, Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Roads.AnyAsync(r => r.Id == roadId && r.UserId == userId, ct);
    }

    public async Task<Guid> SaveFeatureAsync(FeatureBase entity, string featureType, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        FeatureTypeRegistry.AddToDbContext(db, entity);
        db.FeatureRegistry.Add(new FeatureRegistry { Id = entity.Id, FeatureType = featureType });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return entity.Id;
    }

    public async Task<string?> GetFeatureTypeAsync(Guid featureId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var reg = await db.FeatureRegistry.FindAsync([featureId], ct);
        return reg?.FeatureType;
    }

    public async Task<bool> UpdateFeatureAsync(UpdateFeatureCommand command, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
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
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var total = await cleanupService.DeleteAllFeaturesForUserAsync(db, userId, ct);

        await tx.CommitAsync(ct);
        return total;
    }

    public async Task<int> ClearAllRoadsAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var roadIds = await db.Roads
            .Where(r => r.UserId == userId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (roadIds.Count > 0)
        {
            // Roads own their entrances (see DeleteFeatureAsync): the schema has
            // no FK/cascade, so the same bulk contract is enforced here —
            // removing all roads must not leave orphaned house_entrances rows.
            var entranceIds = await db.HouseEntrances
                .Where(e => e.RoadId != null && roadIds.Contains(e.RoadId.Value))
                .Select(e => e.Id)
                .ToListAsync(ct);

            if (entranceIds.Count > 0)
            {
                await db.FeatureRegistry
                    .Where(r => entranceIds.Contains(r.Id))
                    .ExecuteDeleteAsync(ct);
                await db.HouseEntrances
                    .Where(e => e.RoadId != null && roadIds.Contains(e.RoadId.Value))
                    .ExecuteDeleteAsync(ct);
            }

            await db.FeatureRegistry
                .Where(r => roadIds.Contains(r.Id))
                .ExecuteDeleteAsync(ct);
            await db.Roads
                .Where(r => roadIds.Contains(r.Id))
                .ExecuteDeleteAsync(ct);
        }

        // Accepted road drafts mean "a road was materialized from this draft".
        // Once their roads are gone the drafts no longer describe live data, so
        // revert them to pending: otherwise the next AI re-detection dedups
        // against them and the user can never regenerate the commune's roads.
        // Drafts are commune-scoped, so resolve the caller's commune from the
        // user row rather than from the deleted road ids.
        var communeId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.CommuneId)
            .SingleOrDefaultAsync(ct);

        if (communeId is not null)
        {
            await ResetRoadDraftsForClearAsync(db, communeId.Value, ct);
        }

        await tx.CommitAsync(ct);
        return roadIds.Count;
    }

    /// <summary>
    /// Reverts accepted road drafts for <paramref name="communeId"/> back to
    /// pending after their roads were cleared, leaving the review queue reusable
    /// for regeneration. Uses a PostgreSQL-only conditional update; kept virtual
    /// so unit tests can substitute a tracked-equivalent on the InMemory
    /// provider (same convention as RoadGenerationService).
    /// </summary>
    protected virtual async Task<int> ResetRoadDraftsForClearAsync(
        AppDbContext db, int communeId, CancellationToken ct)
    {
        var affected = await db.AiDraftFeatures
            .Where(f => f.CommuneId == communeId
                && f.FeatureType == AiDraftFeature.TypeRoad
                && f.Status == AiDraftFeature.StatusAccepted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.Status, AiDraftFeature.StatusPending)
                .SetProperty(f => f.ReviewedBy, (Guid?)null)
                .SetProperty(f => f.ReviewedAt, (DateTimeOffset?)null), ct);

        await db.SaveChangesAsync(ct);
        return affected;
    }

    public async ValueTask QueueScatteredRefreshAsync(Guid userId, int? communeId)
    {
        if (communeId is null)
        {
            return;
        }

        var enqueued = await bgQueue.QueueBackgroundWorkItemAsync(async (sp, ct) =>
        {
            try
            {
                var svc = sp.GetRequiredService<IScatteredAreaService>();
                // Background path: the recomputed GeoJSON is not streamed to any
                // caller, so it is intentionally discarded here.
                _ = await svc.RefreshAsync(userId, communeId.Value, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background refresh of scattered area failed");
            }
        });

        if (!enqueued)
        {
            // The recompute was rejected because the bounded queue is full: the
            // save/update/delete still succeeded, but the caller has no way to
            // know the scattered layer is now stale. Surface it so the gap
            // between the acknowledged write and the recompute is visible.
            logger.LogWarning(
                "Scattered-area recompute for user {UserId} commune {CommuneId} was dropped: background queue is full.",
                userId, communeId.Value);
        }
    }
}
