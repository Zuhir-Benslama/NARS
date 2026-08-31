using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace NarsApi.Infrastructure;

/// <summary>
/// Configuration model for rate limiting policies.
/// Move values to appsettings.json for easy tuning without recompilation.
/// </summary>
public class RateLimitOptions
{
    // Auth endpoint limits
    public int AuthPermitLimit { get; set; } = 5;
    public int AuthWindowSeconds { get; set; } = 30;
    public int AuthSegmentsPerWindow { get; set; } = 3;

    // Clear endpoint limits
    public int ClearPermitLimit { get; set; } = 3;
    public int ClearWindowMinutes { get; set; } = 10;

    // Scattered-area recompute endpoint limits (deliberately strict — the
    // recompute is a heavy PostGIS operation that runs synchronously on the
    // request thread so the frontend receives the computed GeoJSON).
    public int ScatteredRefreshPermitLimit { get; set; } = 5;
    public int ScatteredRefreshWindowMinutes { get; set; } = 5;

    // General API limits
    public int ApiPermitLimit { get; set; } = 60;
    public int ApiWindowMinutes { get; set; } = 1;
    public int ApiSegmentsPerWindow { get; set; } = 6;

    /// <summary>
    /// How many excess requests may wait (OldestFirst) before being rejected.
    /// Smooths bursty interactive data-entry traffic behind a shared client IP
    /// (NAT) so a temporary spike degrades to a brief pause rather than a hard
    /// 429 mid-work. 0 keeps the strict drop-on-overflow behaviour.
    /// </summary>
    public int ApiQueueLimit { get; set; } = 10;

    // Client-side log submission limits
    public int LogsPermitLimit { get; set; } = 30;
    public int LogsWindowMinutes { get; set; } = 1;
}

/// <summary>
/// Extension methods for configuring rate limiting.
/// </summary>
public static class RateLimitExtensions
{
    /// <summary>
    /// Adds sliding-window rate limiters for auth, clear, and general API endpoints.
    /// Values are read from the "RateLimit" section of configuration.
    /// </summary>
    public static IServiceCollection AddNarsRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(rateOptions =>
        {
            rateOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                // kubelet liveness/readiness probes hit the /health* endpoints
                // every few seconds from the same pod IP; throttling them can
                // crash-loop the deployment and consumes the per-IP quota meant
                // for real API traffic, so exempt them from the global limiter.
                var path = httpContext.Request.Path;
                if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.ApiPermitLimit,
                        Window = TimeSpan.FromMinutes(options.ApiWindowMinutes),
                        QueueLimit = options.ApiQueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    });
            });

            rateOptions.AddSlidingWindowLimiter("auth", limiter =>
            {
                limiter.PermitLimit = options.AuthPermitLimit;
                limiter.Window = TimeSpan.FromSeconds(options.AuthWindowSeconds);
                limiter.SegmentsPerWindow = options.AuthSegmentsPerWindow;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiter.QueueLimit = 0;
            });

            // "clear" uses a fixed window intentionally: deleting all features is an
            // infrequent admin-level action, not a latency-sensitive user operation.
            // A fixed window is simpler and sufficient — bursting 3 requests in the
            // first second then waiting 10 minutes is not a real concern here.
            rateOptions.AddFixedWindowLimiter("clear", limiter =>
            {
                limiter.PermitLimit = options.ClearPermitLimit;
                limiter.Window = TimeSpan.FromMinutes(options.ClearWindowMinutes);
            });

            // "scattered" uses a fixed window for the synchronous scattered-area
            // recompute: it is an explicit, infrequent trigger that can block the
            // request thread for seconds, so abuse must be throttled.
            rateOptions.AddFixedWindowLimiter("scattered", limiter =>
            {
                limiter.PermitLimit = options.ScatteredRefreshPermitLimit;
                limiter.Window = TimeSpan.FromMinutes(options.ScatteredRefreshWindowMinutes);
            });

            rateOptions.AddSlidingWindowLimiter("api", limiter =>
            {
                limiter.PermitLimit = options.ApiPermitLimit;
                limiter.Window = TimeSpan.FromMinutes(options.ApiWindowMinutes);
                limiter.SegmentsPerWindow = options.ApiSegmentsPerWindow;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiter.QueueLimit = 0;
            });

            rateOptions.AddFixedWindowLimiter("logs", limiter =>
            {
                limiter.PermitLimit = options.LogsPermitLimit;
                limiter.Window = TimeSpan.FromMinutes(options.LogsWindowMinutes);
            });
        });

        return services;
    }
}
