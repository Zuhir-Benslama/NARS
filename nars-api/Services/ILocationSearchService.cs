using NarsApi.DTOs;

namespace NarsApi.Services;

/// <summary>
/// Paginated search for administrative reference data (wilayas, dairas, communes).
/// </summary>
public interface ILocationSearchService
{
    /// <summary>Searches wilayas with pagination.</summary>
    Task<PagedResponse<WilayaItem>> SearchWilayasAsync(string search, int skip, int take, CancellationToken ct = default);
    /// <summary>Searches dairas within a wilaya with pagination.</summary>
    Task<PagedResponse<DairaItem>?> SearchDairasAsync(int wilayaId, string search, int skip, int take, CancellationToken ct = default);
    /// <summary>Searches communes within a daira with pagination.</summary>
    Task<PagedResponse<CommuneItem>?> SearchCommunesAsync(int dairaId, string search, int skip, int take, CancellationToken ct = default);
}
