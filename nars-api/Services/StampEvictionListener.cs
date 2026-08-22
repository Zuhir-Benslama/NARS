using Microsoft.Extensions.Hosting;
using Npgsql;

namespace NarsApi.Services;

/// <summary>
/// Listens for Postgres NOTIFY events on the stamp-eviction channel and evicts
/// the local security-stamp cache entry for the affected user. A database
/// trigger (migration AddStampEvictionNotifyTrigger) fires the notification
/// whenever any replica writes a changed SecurityStamp, so every node drops its
/// cached stamp immediately instead of serving a stale entry until TTL expiry.
/// This closes the multi-node invalidation window of the per-node cache without
/// introducing an external dependency such as Redis.
/// </summary>
public sealed class StampEvictionListener(
    string connectionString,
    ISecurityStampCache stampCache,
    ILogger<StampEvictionListener> logger) : BackgroundService
{
    internal const string ChannelName = "nars_stamp_evict";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(stoppingToken);

                conn.Notification += OnNotification;
                await using (var cmd = conn.CreateCommand())
                {
                    // Channel name is a compile-time constant, not user input.
                    cmd.CommandText = $"LISTEN {ChannelName}";
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }

                logger.LogInformation(
                    "Listening for security-stamp evictions on '{Channel}'", ChannelName);

                // WaitAsync blocks until a notification arrives or the connection
                // drops; the loop re-establishes LISTEN after either event.
                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Security-stamp listener disconnected; retrying in {Delay}s",
                    ReconnectDelay.TotalSeconds);
                try
                {
                    await Task.Delay(ReconnectDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs e) =>
        EvictFromPayload(e.Payload ?? string.Empty, stampCache, logger);

    internal static void EvictFromPayload(string payload, ISecurityStampCache cache, ILogger logger)
    {
        if (Guid.TryParse(payload.Trim(), out var userId))
        {
            cache.EvictStamp(userId);
            logger.LogDebug("Evicted cached security stamp for {UserId} via notification", userId);
        }
        else if (!string.IsNullOrWhiteSpace(payload))
        {
            logger.LogWarning(
                "Ignoring malformed stamp-eviction payload: {Payload}",
                payload.ReplaceLineEndings(" "));
        }
    }
}
