using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

/// <summary>
/// Periodically deletes revoked and expired refresh tokens. Every rotation and
/// issuance inserts a row and nothing ever removed one, so without this the
/// table grows without bound. Runs on a fixed interval; a failed run (e.g. DB
/// briefly down) is logged and retried on the next tick.
/// </summary>
public sealed class RefreshTokenPruner(
    IServiceProvider services,
    IOptions<RefreshTokenPruningOptions> options,
    IDateTimeProvider timeProvider,
    ILogger<RefreshTokenPruner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait one full interval before the first prune so a slow DB during
        // startup never blocks readiness; retries are just later ticks.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(options.Value.IntervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var deleted = await PruneAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("[Maintenance] Pruned {Count} revoked/expired refresh tokens.", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Maintenance] Refresh-token pruning failed; will retry next interval.");
            }
        }
    }

    private async Task<int> PruneAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.UtcNow;
        return await db.RefreshTokens
            .Where(rt => rt.Revoked || rt.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);
    }
}
