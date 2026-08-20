namespace NarsApi.Infrastructure;

/// <summary>
/// Extension methods for CORS and response compression configuration.
/// </summary>
public static class CorsCompressionExtensions
{
    /// <summary>
    /// Adds CORS with explicit origins and credentials support (required for HttpOnly cookie auth).
    /// Logs a warning if only localhost origins are configured in non-development environments.
    /// </summary>
    public static IServiceCollection AddNarsCors(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env)
    {
        var allowedOrigins = config
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? ["http://localhost:5000", "http://localhost:5001",
                "https://localhost:7000", "https://localhost:7001"];

        // Capture configuration state for post-build validation (avoids
        // BuildServiceProvider() anti-pattern). Checked in ConfigureNarsPipelineAsync.
        if (!env.IsDevelopment() && allowedOrigins.All(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            services.AddSingleton(new CorsOriginWarning { EnvironmentName = env.EnvironmentName });
        }

        services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(allowedOrigins)
                      .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                      .WithHeaders("Authorization", "Content-Type", "X-CSRF-Token", "X-Admin-Signup")
                      .AllowCredentials()
            )
        );

        return services;
    }

    /// <summary>
    /// Adds response compression for GeoJSON/JSON payloads (Brotli + gzip).
    /// </summary>
    public static IServiceCollection AddNarsCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes =
            [
                "text/plain",
                "text/html",
                "text/css",
                "text/javascript",
                "application/javascript",
                "application/json",
                "application/geo+json",
                "application/vnd.geo+json",
                "application/xml",
                "text/xml",
            ];
        });

        return services;
    }
}

/// <summary>
/// Marker type captured during DI registration when CORS origins contain only
/// localhost in a non-development environment. Logged as a warning once after
/// the service provider is built (avoids BuildServiceProvider() anti-pattern).
/// </summary>
internal sealed class CorsOriginWarning
{
    public required string EnvironmentName { get; init; }
}
