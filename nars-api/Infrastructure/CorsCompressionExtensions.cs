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

        if (!env.IsDevelopment() && allowedOrigins.All(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            var logger = services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
            logger.LogWarning(
                "[Security] CORS:AllowedOrigins contains only localhost origins in {Environment} environment. " +
                "Set Cors:AllowedOrigins to the actual production domain(s).",
                env.EnvironmentName);
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
