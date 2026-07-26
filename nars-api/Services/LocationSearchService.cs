using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Services;

public class LocationSearchService(IDbContextFactory<AppDbContext> dbFactory) : ILocationSearchService
{
    public async Task<PagedResponse<WilayaItem>> SearchWilayasAsync(string search, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Wilayas.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(w => EF.Functions.ILike(w.WilayaFr ?? "", $"%{search}%")
                           || EF.Functions.ILike(w.WilayaAr ?? "", $"%{search}%"));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(w => w.WilayaFr).Skip(skip).Take(take)
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
            q = q.Where(d => EF.Functions.ILike(d.DairaFr, $"%{search}%")
                           || EF.Functions.ILike(d.DairaAr, $"%{search}%"));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(d => d.DairaFr).Skip(skip).Take(take)
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
            q = q.Where(c => EF.Functions.ILike(c.CommuneFr, $"%{search}%")
                           || EF.Functions.ILike(c.CommuneAr, $"%{search}%"));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(c => c.CommuneFr).Skip(skip).Take(take)
            .Select(c => new CommuneItem(c.CommuneId, c.CommuneFr, c.CommuneAr, c.CommuneCode == null ? null : c.CommuneCode.ToString(), c.CommuneLatitude, c.CommuneLongitude, c.CommuneName ?? ""))
            .ToListAsync(ct);

        return new PagedResponse<CommuneItem>(items, total, skip, take);
    }
}
