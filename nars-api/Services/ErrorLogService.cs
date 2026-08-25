using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class ErrorLogService(IDbContextFactory<AppDbContext> dbFactory, IOptions<LoggingOptions> loggingOptions) : IErrorLogService
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
            entries = [.. entries.Take(maxSize)];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ErrorLogs.AddRange(entries);
        await db.SaveChangesAsync(ct);
    }
}
