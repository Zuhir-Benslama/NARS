using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public class LocationQueryService(IDbContextFactory<AppDbContext> dbFactory) : ILocationQueryService
{
    public async Task<List<Wilaya>> GetAllWilayasAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Wilayas.ToListAsync(ct);
    }

    public async Task<List<Daira>> GetDairasByWilayaAsync(int wilayaId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Dairas.Where(d => d.WilayaId == wilayaId).ToListAsync(ct);
    }

    public async Task<List<Commune>> GetCommunesByDairaAsync(int dairaId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Communes.Where(c => c.DairaId == dairaId).ToListAsync(ct);
    }

    public async Task<Commune?> GetCommuneByIdAsync(int communeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Communes.FirstOrDefaultAsync(c => c.CommuneId == communeId, ct);
    }

    public async Task<CommuneBoundary?> GetCommuneBoundaryAsync(int communeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CommuneBoundaries.FirstOrDefaultAsync(b => b.CommuneId == communeId, ct);
    }

    public async Task<(Commune? Commune, Daira? Daira)> GetCommuneWithDairaAsync(int communeId, CancellationToken ct = default)
    {
        await using var context = await dbFactory.CreateDbContextAsync(ct);
        var result = await (
            from c in context.Communes
            where c.CommuneId == communeId
            join d in context.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d }
        ).FirstOrDefaultAsync(ct);
        return result is null ? (null, null) : (result.Commune, result.Daira);
    }

    public async Task<(Commune? Commune, Daira? Daira, Wilaya? Wilaya)> GetLocationChainAsync(int communeId, CancellationToken ct = default)
    {
        await using var context = await dbFactory.CreateDbContextAsync(ct);
        var result = await (
            from c in context.Communes
            where c.CommuneId == communeId
            join d in context.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            join w in context.Wilayas on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync(ct);
        return result is null ? (null, null, null) : (result.Commune, result.Daira, result.Wilaya);
    }

    public async Task<(Daira? Daira, Wilaya? Wilaya)> GetDairaWithWilayaAsync(int dairaId, CancellationToken ct = default)
    {
        await using var context = await dbFactory.CreateDbContextAsync(ct);
        var result = await (
            from d in context.Dairas
            where d.DairaId == dairaId
            join w in context.Wilayas on d.WilayaId equals w.WilayaId into wj
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
        return await context.Wilayas.FindAsync([wilayaId.Value], ct);
    }
}
