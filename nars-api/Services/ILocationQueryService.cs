using NarsApi.Models;

namespace NarsApi.Services;

public interface ILocationQueryService
{
    Task<List<Wilaya>> GetAllWilayasAsync(CancellationToken ct = default);
    Task<List<Daira>> GetDairasByWilayaAsync(int wilayaId, CancellationToken ct = default);
    Task<List<Commune>> GetCommunesByDairaAsync(int dairaId, CancellationToken ct = default);
    Task<Commune?> GetCommuneByIdAsync(int communeId, CancellationToken ct = default);
    Task<CommuneBoundary?> GetCommuneBoundaryAsync(int communeId, CancellationToken ct = default);
}
