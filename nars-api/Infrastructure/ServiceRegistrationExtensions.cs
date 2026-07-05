using Microsoft.AspNetCore.Http.Features;
using NarsApi.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace NarsApi.Infrastructure;

public static class ServiceRegistrationExtensions
{
    /// <summary>
    /// Registers all NARS application services, EF Core DbContext, authentication,
    /// OpenTelemetry, health checks, CORS, compression, rate limiting, and
    /// background task infrastructure.
    /// Call this from <c>Program.cs</c> during the application's service collection phase.
    /// </summary>
    public static IServiceCollection AddNarsServices(
        this IServiceCollection services,
        IConfiguration config,
        string connectionString,
        string jwtSecret,
        string? jwtIssuer,
        string? jwtAudience)
    {
        // ── Register typed options ────────────────────────────────
        services.Configure<CacheOptions>(config.GetSection("Cache"));
        services.Configure<LocationsOptions>(config.GetSection("Locations"));
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.Configure<FeatureDefaultsOptions>(config.GetSection("FeatureDefaults"));
        services.Configure<LoggingOptions>(config.GetSection("Logging"));
        services.Configure<ValidationOptions>(config.GetSection("Validation"));
        services.Configure<AccountLockoutOptions>(config.GetSection("AccountLockout"));
        services.Configure<OpenTelemetryOptions>(config.GetSection("OpenTelemetry"));
        services.Configure<BackgroundTaskOptions>(config.GetSection("BackgroundTask"));

        var otelOpts = config.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();
        var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? otelOpts.OtlpEndpoint;

        var assemblyVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0.0";
        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService("nars-api", serviceVersion: assemblyVersion)
                .AddEnvironmentVariableDetector())
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("DefaultConnection is not configured.");
        }

        services.AddNarsDatabase(connectionString);

        services.AddNarsJwtAuthentication(jwtSecret, issuer: jwtIssuer, audience: jwtAudience);

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddHostedService<BackgroundQueueProcessor>();
        services.AddScoped<IScatteredAreaService, ScatteredAreaService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IValidationService, ValidationService>();
        services.AddScoped<IFieldService, FieldService>();
        services.AddScoped<IFeatureStatsService, FeatureStatsService>();
        services.AddScoped<IFeatureRepository, FeatureRepository>();
        services.AddScoped<IBoundaryService, BoundaryService>();
        services.AddScoped<IEntranceQueryService, EntranceQueryService>();
        services.AddScoped<IAdminOverviewService, AdminOverviewService>();

        var httpOpts = config.GetSection("HttpClient").Get<HttpClientOptions>() ?? new HttpClientOptions();
        services.AddHttpClient("tile-proxy", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(httpOpts.TileProxyTimeoutSeconds);
            client.DefaultRequestHeaders.Add("User-Agent", "NARS-TileProxy/1.0");
        });

        services.AddHttpClient("satellite", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(httpOpts.SatelliteTimeoutSeconds);
            client.DefaultRequestHeaders.Add("User-Agent", "NARS-Satellite/1.0");
        });

        services.AddMemoryCache();

        services.AddControllers()
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });

        services.AddNarsCors(config);
        services.AddNarsCompression();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info.Title = "NARS - National Addressing Reference System";
                doc.Info.Description = "Geographic data management API";
                doc.Info.Version = assemblyVersion;
                return Task.CompletedTask;
            });
        });

        services.AddNarsRateLimiting(config);

        services.AddHealthChecks()
            .AddNarsDatabaseHealthCheck(connectionString);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "X-CSRF-TOKEN-COOKIE";
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        var featureOpts = config.GetSection("FeatureDefaults").Get<FeatureDefaultsOptions>() ?? new FeatureDefaultsOptions();
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = featureOpts.MultipartBodyLengthLimit;
            options.ValueLengthLimit = featureOpts.ValueLengthLimit;
        });

        return services;
    }
}
