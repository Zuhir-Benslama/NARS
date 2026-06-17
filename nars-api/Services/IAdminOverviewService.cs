using NarsApi.DTOs;

namespace NarsApi.Services;

public interface IAdminOverviewService
{
    Task<List<WilayaSummary>> GetNationalOverviewAsync(CancellationToken cancellationToken = default);
    Task<WilayaReport?> GetWilayaReportAsync(int wilayaId, CancellationToken cancellationToken = default);
    Task<DairaReport?> GetDairaReportAsync(int dairaId, CancellationToken cancellationToken = default);
}
