using NarsApi.Models;

namespace NarsApi.Services;

public interface IErrorLogService
{
    /// <summary>Persists a batch of client-side error log entries.</summary>
    Task LogBatchAsync(List<ErrorLog> entries, CancellationToken ct = default);
}
