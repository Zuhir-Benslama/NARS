using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NarsApi.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace NarsApi.Infrastructure;

public static class ServiceRegistrationExtensions
{
    public const string DefaultAssemblyVersion = "2.0.0";

    private static readonly string AssemblyVersion =
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? DefaultAssemblyVersion;

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
        string? jwtAudience,
        IHostEnvironment env)
    {
        services.AddNarsOptions(config);
        services.AddNarsOpenTelemetry(config);
        services.AddNarsDatabase(connectionString);
        // Cluster-wide security-stamp invalidation: the DB trigger (migration
        // AddStampEvictionNotifyTrigger) notifies this listener on stamp change.
        services.AddHostedService(sp => new StampEvictionListener(
            connectionString,
            sp.GetRequiredService<ISecurityStampCache>(),
            sp.GetRequiredService<ILogger<StampEvictionListener>>()));
        var jwtAlgorithm = config.GetSection("Jwt").Get<JwtOptions>()?.Algorithm ?? "HS256";
        services.AddNarsJwtAuthentication(jwtSecret, issuer: jwtIssuer, audience: jwtAudience, algorithm: jwtAlgorithm);
        services.AddNarsDomainServices();
        services.AddNarsHttpClients(config);
        services.AddNarsControllers();
        var corsOptions = config.GetSection("Cors").Get<CorsOptions>() ?? new CorsOptions();
        services.AddNarsCors(corsOptions, env);
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
        services.AddOptions<RefreshTokenPruningOptions>().Bind(config.GetSection("RefreshTokenPruning"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AdminSignupOptions>().Bind(config.GetSection("AdminSignup"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CspOptions>().Bind(config.GetSection("Csp"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ProxyOptions>().Bind(config.GetSection("ForwardedHeaders"))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CorsOptions>().Bind(config.GetSection("Cors"))
            .ValidateDataAnnotations().ValidateOnStart();
    }

    private static void AddNarsOpenTelemetry(this IServiceCollection services, IConfiguration config)
    {
        var otelOpts = config.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();
        var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? otelOpts.OtlpEndpoint;

        var builder = services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService("nars-api", serviceVersion: AssemblyVersion)
                .AddEnvironmentVariableDetector());

        // Graceful fallback: without an explicit endpoint (dev), run without an
        // OTLP exporter instead of hammering an unreachable collector with logs.
        if (Uri.TryCreate(otelEndpoint, UriKind.Absolute, out var otlpEndpointUri))
        {
            builder.WithTracing(t => t
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = otlpEndpointUri))
                .WithMetrics(m => m
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddOtlpExporter(o => o.Endpoint = otlpEndpointUri));
        }
    }

    private static void AddNarsDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddHostedService<BackgroundQueueProcessor>();
        services.AddHostedService<RefreshTokenPruner>();
        services.AddSingleton<IScatteredAreaService, ScatteredAreaService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IValidationService, ValidationService>();
        services.AddScoped<IFieldService, FieldService>();
        services.AddScoped<IFeatureStatsService, FeatureStatsService>();
        services.AddScoped<IFeatureService, FeatureService>();
        services.AddScoped<IBoundaryService, BoundaryService>();
        services.AddScoped<IEntranceQueryService, EntranceQueryService>();
        services.AddScoped<INumberEntrancesService, NumberEntrancesService>();
        services.AddScoped<IAdminOverviewService, AdminOverviewService>();
        services.AddScoped<IUserAuthorizationService, UserAuthorizationService>();
        services.AddScoped<IErrorLogService, ErrorLogService>();
        services.AddScoped<ILocationQueryService, LocationQueryService>();
        services.AddScoped<ILocationSearchService, LocationSearchService>();
        services.AddScoped<IRoadQueryService, RoadQueryService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IUserCreationService, UserCreationService>();
        services.AddScoped<ICommuneScopeService, CommuneScopeService>();
        services.AddScoped<IDraftFeaturesService, DraftFeaturesService>();
        services.AddSingleton<ILogSanitizer, LogSanitizer>();
        services.AddSingleton<ISecurityStampCache, SecurityStampCache>();
        services.AddScoped<IPageAuthService, PageAuthService>();
        services.AddScoped<IFeatureCleanupService, FeatureCleanupService>();
    }

    private static void AddNarsHttpClients(this IServiceCollection services, IConfiguration config) => services.AddSegmentationClient(config);

    private static void AddNarsControllers(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
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
                doc.Info.Version = AssemblyVersion;
                return Task.CompletedTask;
            });
        });
    }

    private static void AddNarsHealthChecks(this IServiceCollection services, string connectionString) => services.AddHealthChecks()
            .AddNarsDatabaseHealthCheck(connectionString);

    private static void AddNarsAntiforgery(this IServiceCollection services) => services.AddAntiforgery(options =>
                                                                                     {
                                                                                         options.HeaderName = "X-CSRF-Token";
                                                                                         options.Cookie.Name = "X-CSRF-TOKEN-COOKIE";
                                                                                         // Intentionally NOT HttpOnly: this is a double-submit CSRF
                                                                                         // token cookie that the SPA must read (via JS) to send back
                                                                                         // in the X-CSRF-Token header. Making it HttpOnly would break
                                                                                         // every POST/PATCH/DELETE request. The cookie carries no session
                                                                                         // material — the access/refresh JWTs are in separate cookies.
                                                                                         // (CodeQL cs/web/cookie-httponly-not-set — false positive.)
                                                                                         options.Cookie.HttpOnly = false;
                                                                                         options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                                                                                     });

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
