using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class LocationSearchService(IDbContextFactory<AppDbContext> dbFactory) : ILocationSearchService
{
    public async Task<PagedResponse<WilayaItem>> SearchWilayasAsync(string search, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Wilayas.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var escaped = SqlFragments.EscapeLikeWildcards(search);
            q = q.Where(w => EF.Functions.ILike(w.WilayaFr ?? "", $"%{escaped}%", @"\")
                           || EF.Functions.ILike(w.WilayaAr ?? "", $"%{escaped}%", @"\"));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(w => w.WilayaFr).ThenBy(w => w.WilayaId).Skip(skip).Take(take)
            .Select(w => new WilayaItem(w.WilayaId, w.WilayaFr ?? "", w.WilayaAr ?? "", w.WilayaLatitude, w.WilayaLongitude))
            .ToListAsync(ct);

        return new PagedResponse<WilayaItem>(items, total, skip, take);
    }

    public async Task<PagedResponse<DairaItem>?> SearchDairasAsync(int wilayaId, string search, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Dairas.Where(d => d.WilayaId == wilayaId).AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var escaped = SqlFragments.EscapeLikeWildcards(search);
            q = q.Where(d => EF.Functions.ILike(d.DairaFr, $"%{escaped}%", @"\")
                           || EF.Functions.ILike(d.DairaAr, $"%{escaped}%", @"\"));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(d => d.DairaFr).ThenBy(d => d.DairaId).Skip(skip).Take(take)
            .Select(d => new DairaItem(d.DairaId, d.DairaFr, d.DairaAr, d.DairaLatitude, d.DairaLongitude, d.DairaName ?? ""))
            .ToListAsync(ct);

        return new PagedResponse<DairaItem>(items, total, skip, take);
    }

    public async Task<PagedResponse<CommuneItem>?> SearchCommunesAsync(int dairaId, string search, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Communes.Where(c => c.DairaId == dairaId).AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var escaped = SqlFragments.EscapeLikeWildcards(search);
            q = q.Where(c => EF.Functions.ILike(c.CommuneFr, $"%{escaped}%", @"\")
                           || EF.Functions.ILike(c.CommuneAr, $"%{escaped}%", @"\"));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(c => c.CommuneFr).ThenBy(c => c.CommuneId).Skip(skip).Take(take)
            .Select(c => new CommuneItem(
                c.CommuneId,
                c.CommuneFr,
                c.CommuneAr,
                c.CommuneCode == null ? null : c.CommuneCode.ToString(),
                c.CommuneLatitude,
                c.CommuneLongitude,
                c.CommuneName ?? ""))
            .ToListAsync(ct);

        return new PagedResponse<CommuneItem>(items, total, skip, take);
    }
}
