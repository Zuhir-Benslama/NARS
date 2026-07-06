using NarsApi.Models;

namespace NarsApi.Services;

public interface IErrorLogService
{
    Task LogBatchAsync(List<ErrorLog> entries, CancellationToken ct = default);
}
