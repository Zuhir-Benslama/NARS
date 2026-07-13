namespace NarsApi.Infrastructure;

/// <summary>
/// Extension methods for CORS and response compression configuration.
/// </summary>
public static class CorsCompressionExtensions
{
    /// <summary>
    /// Adds CORS with explicit origins and credentials support (required for HttpOnly cookie auth).
    /// </summary>
    public static IServiceCollection AddNarsCors(
        this IServiceCollection services,
        IConfiguration config)
    {
        var allowedOrigins = config
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? ["http://localhost:5000", "http://localhost:5001",
                "https://localhost:7000", "https://localhost:7001"];

        services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy.WithOrigins(allowedOrigins)
                      .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                      .WithHeaders("Authorization", "Content-Type", "X-CSRF-Token")
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
