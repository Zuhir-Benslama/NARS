using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class LocationQueryService(IDbContextFactory<AppDbContext> dbFactory) : ILocationQueryService
{
    public async Task<Commune?> GetCommuneByIdAsync(int communeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Communes.AsNoTracking().FirstOrDefaultAsync(c => c.CommuneId == communeId, ct);
    }

    public async Task<CommuneBoundary?> GetCommuneBoundaryAsync(int communeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CommuneBoundaries.AsNoTracking().FirstOrDefaultAsync(b => b.CommuneId == communeId, ct);
    }

    public async Task<(Commune? Commune, Daira? Daira)> GetCommuneWithDairaAsync(int communeId, CancellationToken ct = default)
    {
        await using var context = await dbFactory.CreateDbContextAsync(ct);
        var result = await (
            from c in context.Communes.AsNoTracking()
            where c.CommuneId == communeId
            join d in context.Dairas.AsNoTracking() on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d }
        ).FirstOrDefaultAsync(ct);
        return result is null ? (null, null) : (result.Commune, result.Daira);
    }

    public async Task<(Commune? Commune, Daira? Daira, Wilaya? Wilaya)> GetLocationChainAsync(int communeId, CancellationToken ct = default)
    {
        await using var context = await dbFactory.CreateDbContextAsync(ct);
        var result = await (
            from c in context.Communes.AsNoTracking()
            where c.CommuneId == communeId
            join d in context.Dairas.AsNoTracking() on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            join w in context.Wilayas.AsNoTracking() on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync(ct);
        return result is null ? (null, null, null) : (result.Commune, result.Daira, result.Wilaya);
    }

    public async Task<(Daira? Daira, Wilaya? Wilaya)> GetDairaWithWilayaAsync(int dairaId, CancellationToken ct = default)
    {
        await using var context = await dbFactory.CreateDbContextAsync(ct);
        var result = await (
            from d in context.Dairas.AsNoTracking()
            where d.DairaId == dairaId
            join w in context.Wilayas.AsNoTracking() on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Daira = d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync(ct);
        return result is null ? (null, null) : (result.Daira, result.Wilaya);
    }

    public async Task<Wilaya?> GetWilayaAsync(int? wilayaId, CancellationToken ct = default)
    {
        if (!wilayaId.HasValue)
        {
            return null;
        }

        await using var context = await dbFactory.CreateDbContextAsync(ct);
        return await context.Wilayas.AsNoTracking().FirstOrDefaultAsync(w => w.WilayaId == wilayaId.Value, ct);
    }
}
