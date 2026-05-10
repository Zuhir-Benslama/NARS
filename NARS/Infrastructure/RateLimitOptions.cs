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

    // General API limits
    public int ApiPermitLimit { get; set; } = 60;
    public int ApiWindowMinutes { get; set; } = 1;
    public int ApiSegmentsPerWindow { get; set; } = 6;

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
