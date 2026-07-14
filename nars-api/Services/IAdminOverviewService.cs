using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Services;

public interface IAdminOverviewService
{
    /// <summary>Returns summary data for all wilayas (national dashboard).</summary>
    Task<List<WilayaSummary>> GetNationalOverviewAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns a detailed report for a specific wilaya, or null.</summary>
    Task<WilayaReport?> GetWilayaReportAsync(int wilayaId, CancellationToken cancellationToken = default);
    /// <summary>Returns a detailed report for a specific daira, or null.</summary>
    Task<DairaReport?> GetDairaReportAsync(int dairaId, CancellationToken cancellationToken = default);
    /// <summary>Returns the daira entity by ID, or null.</summary>
    Task<Daira?> GetDairaByIdAsync(int dairaId, CancellationToken cancellationToken = default);
}
