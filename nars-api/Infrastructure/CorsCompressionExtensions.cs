namespace NarsApi.Infrastructure;

/// <summary>
/// Extension methods for CORS and response compression configuration.
/// </summary>
public static class CorsCompressionExtensions
{
    /// <summary>
    /// Adds CORS with explicit origins and credentials support (required for HttpOnly cookie auth).
    /// Fails fast outside Development when CORS origins are not explicitly configured:
    /// silently serving cross-origin credentials to localhost defaults would fail open.
    /// The <paramref name="corsOptions"/> instance is the same bound <c>Cors</c>
    /// configuration section the CSRF origin-rejection middleware resolves via
    /// <c>IOptions&lt;CorsOptions&gt;</c>, so both enforce one allowlist.
    /// </summary>
    public static IServiceCollection AddNarsCors(
        this IServiceCollection services,
        CorsOptions corsOptions,
        IHostEnvironment env)
    {
        var allowedOrigins = corsOptions.AllowedOrigins;

        // A non-development deployment without explicit origins would accept
        // cross-origin credentialed requests from localhost defaults. Fail at
        // startup instead — matching how missing Jwt:Issuer/Audience is handled.
        if (!env.IsDevelopment() && allowedOrigins.All(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Cors:AllowedOrigins must be configured to the actual origin(s) in the {env.EnvironmentName} environment. " +
                "The localhost defaults are only safe for development.");
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
