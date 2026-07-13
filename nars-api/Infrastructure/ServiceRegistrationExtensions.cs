using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
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
        services.AddNarsOptions(config);
        services.AddNarsOpenTelemetry(config);
        services.AddNarsDatabase(connectionString);
        services.AddNarsJwtAuthentication(jwtSecret, issuer: jwtIssuer, audience: jwtAudience);
        services.AddNarsDomainServices();
        services.AddNarsHttpClients(config);
        var assemblyVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0.0";
        services.AddNarsControllers(assemblyVersion);
        services.AddNarsCors(config);
        services.AddNarsCompression();
        services.AddNarsRateLimiting(config);
        services.AddNarsHealthChecks(connectionString);
        services.AddNarsAntiforgery();
        services.AddNarsFormOptions(config);
        return services;
    }

    private static void AddNarsOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CacheOptions>().Bind(config.GetSection("Cache"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<LocationsOptions>().Bind(config.GetSection("Locations"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<JwtOptions>().Bind(config.GetSection("Jwt"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<FeatureDefaultsOptions>().Bind(config.GetSection("FeatureDefaults"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<LoggingOptions>().Bind(config.GetSection("Logging"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ValidationOptions>().Bind(config.GetSection("Validation"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AccountLockoutOptions>().Bind(config.GetSection("AccountLockout"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<OpenTelemetryOptions>().Bind(config.GetSection("OpenTelemetry"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<BackgroundTaskOptions>().Bind(config.GetSection("BackgroundTask"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CspOptions>().Bind(config.GetSection("Csp"))
            .ValidateDataAnnotations().ValidateOnStart();
    }

    private static void AddNarsOpenTelemetry(this IServiceCollection services, IConfiguration config)
    {
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
    }

    private static void AddNarsDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddHostedService<BackgroundQueueProcessor>();
        services.AddScoped<IScatteredAreaService, ScatteredAreaService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IValidationService, ValidationService>();
        services.AddScoped<IFieldService, FieldService>();
        services.AddScoped<IFeatureStatsService, FeatureStatsService>();
        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<IBoundaryService, BoundaryService>();
        services.AddScoped<IEntranceQueryService, EntranceQueryService>();
        services.AddScoped<IAdminOverviewService, AdminOverviewService>();
        services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
        services.AddScoped<IErrorLogService, ErrorLogService>();
        services.AddScoped<ILocationQueryService, LocationQueryService>();
        services.AddScoped<IRoadQueryService, RoadQueryService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IUserCreationService, UserCreationService>();
    }

    private static void AddNarsHttpClients(this IServiceCollection services, IConfiguration config)
    {
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
    }

    private static void AddNarsControllers(this IServiceCollection services, string assemblyVersion)
    {
        services.AddMemoryCache();
        services.AddControllers()
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });
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
    }

    private static void AddNarsHealthChecks(this IServiceCollection services, string connectionString)
    {
        services.AddHealthChecks()
            .AddNarsDatabaseHealthCheck(connectionString);
    }

    private static void AddNarsAntiforgery(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "X-CSRF-TOKEN-COOKIE";
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });
    }

    private static void AddNarsFormOptions(this IServiceCollection services, IConfiguration config)
    {
        var featureOpts = config.GetSection("FeatureDefaults").Get<FeatureDefaultsOptions>() ?? new FeatureDefaultsOptions();
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = featureOpts.MultipartBodyLengthLimit;
            options.ValueLengthLimit = featureOpts.ValueLengthLimit;
        });
    }
}
