using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public class ErrorLogService(AppDbContext db) : IErrorLogService
{
    public async Task LogBatchAsync(List<ErrorLog> entries, CancellationToken ct = default)
    {
        db.ErrorLogs.AddRange(entries);
        await db.SaveChangesAsync(ct);
    }
}
