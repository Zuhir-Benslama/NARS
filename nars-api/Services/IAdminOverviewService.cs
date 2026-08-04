using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Services;

public interface IAdminOverviewService
{
    /// <summary>Returns paginated summary data for wilayas (national dashboard).</summary>
    Task<(List<WilayaSummary> Items, int Total)> GetNationalOverviewAsync(int skip = 0, int take = 500, CancellationToken cancellationToken = default);
    /// <summary>Returns a detailed report for a specific wilaya, or null.</summary>
    Task<WilayaReport?> GetWilayaReportAsync(int wilayaId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Returns a detailed report for a specific daira, or null. When
    /// <paramref name="expectedWilayaId"/> is set, returns null if the daira does
    /// not belong to that wilaya (caller-scope enforcement for wilaya admins).
    /// </summary>
    Task<DairaReport?> GetDairaReportAsync(int dairaId, int? expectedWilayaId = null, CancellationToken cancellationToken = default);
}
