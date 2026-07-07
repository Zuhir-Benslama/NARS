using NarsApi.Models;

namespace NarsApi.Services;

public interface ILocationQueryService
{
    /// <summary>Returns all wilayas.</summary>
    Task<List<Wilaya>> GetAllWilayasAsync(CancellationToken ct = default);
    /// <summary>Returns dairas belonging to a wilaya.</summary>
    Task<List<Daira>> GetDairasByWilayaAsync(int wilayaId, CancellationToken ct = default);
    /// <summary>Returns communes belonging to a daira.</summary>
    Task<List<Commune>> GetCommunesByDairaAsync(int dairaId, CancellationToken ct = default);
    /// <summary>Returns a commune by ID, or null.</summary>
    Task<Commune?> GetCommuneByIdAsync(int communeId, CancellationToken ct = default);
    /// <summary>Returns the boundary geometry for a commune, or null.</summary>
    Task<CommuneBoundary?> GetCommuneBoundaryAsync(int communeId, CancellationToken ct = default);
}
