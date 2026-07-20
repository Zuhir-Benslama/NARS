using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class ErrorLogService(AppDbContext db, IOptions<LoggingOptions> loggingOptions) : IErrorLogService
{
    public async Task LogBatchAsync(List<ErrorLog> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var maxSize = loggingOptions.Value.MaxBatchSize;
        if (entries.Count > maxSize)
        {
            entries = entries.Take(maxSize).ToList();
        }

        db.ErrorLogs.AddRange(entries);
        await db.SaveChangesAsync(ct);
    }
}
