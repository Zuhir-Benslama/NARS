using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public class LocationQueryService(AppDbContext db) : ILocationQueryService
{
    public Task<List<Wilaya>> GetAllWilayasAsync(CancellationToken ct = default) =>
        db.Wilayas.ToListAsync(ct);

    public Task<List<Daira>> GetDairasByWilayaAsync(int wilayaId, CancellationToken ct = default) =>
        db.Dairas.Where(d => d.WilayaId == wilayaId).ToListAsync(ct);

    public Task<List<Commune>> GetCommunesByDairaAsync(int dairaId, CancellationToken ct = default) =>
        db.Communes.Where(c => c.DairaId == dairaId).ToListAsync(ct);

    public Task<Commune?> GetCommuneByIdAsync(int communeId, CancellationToken ct = default) =>
        db.Communes.FirstOrDefaultAsync(c => c.CommuneId == communeId, ct);

    public Task<CommuneBoundary?> GetCommuneBoundaryAsync(int communeId, CancellationToken ct = default) =>
        db.CommuneBoundaries.FirstOrDefaultAsync(b => b.CommuneId == communeId, ct);
}
