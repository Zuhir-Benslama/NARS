using System.Data;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>Creates house entrances on roads and reads their ownership.</summary>
public interface IEntranceService
{
    Task<(Guid OwnerUserId, int? CommuneId)?> GetRoadOwnerAsync(Guid roadId, CancellationToken ct = default);
    Task<Guid> CreateEntranceAsync(Guid roadId, Guid ownerUserId, Guid creatorUserId, string label, string data, CancellationToken ct = default);
}

public sealed class EntranceService(IDbContextFactory<AppDbContext> dbFactory) : IEntranceService
{
    public async Task<(Guid OwnerUserId, int? CommuneId)?> GetRoadOwnerAsync(Guid roadId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var result = await (
            from r in db.Roads
            join u in db.Users on r.UserId equals u.Id
            where r.Id == roadId
            select new { r.UserId, u.CommuneId }
        ).FirstOrDefaultAsync(ct);

        return result is null ? null : (result.UserId, result.CommuneId);
    }

    public async Task<Guid> CreateEntranceAsync(Guid roadId, Guid ownerUserId, Guid creatorUserId, string label, string data, CancellationToken ct = default)
    {
        var newId = Guid.CreateVersion7();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
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
}
