using Microsoft.AspNetCore.Http.Features;
using NarsApi.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace NarsApi.Infrastructure;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddNarsServices(
        this IServiceCollection services,
        IConfiguration config,
        string connectionString,
        string jwtSecret,
        string? jwtIssuer,
        string? jwtAudience)
    {
        var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? "http://otel-collector.observability:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService("nars-api", serviceVersion: "2.0.0")
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

        var tileTimeout = int.TryParse(config["HttpClient:TileProxyTimeoutSeconds"], out var tts) ? tts : 15;
        services.AddHttpClient("tile-proxy", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(tileTimeout);
            client.DefaultRequestHeaders.Add("User-Agent", "NARS-TileProxy/1.0");
        });

        var satTimeout = int.TryParse(config["HttpClient:SatelliteTimeoutSeconds"], out var sts) ? sts : 30;
        services.AddHttpClient("satellite", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(satTimeout);
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
                doc.Info.Version = "2.0.0";
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

        var multipartLimit = int.TryParse(config["FeatureDefaults:MultipartBodyLengthLimit"], out var mll) ? mll : 10_485_760;
        var valueLimit = int.TryParse(config["FeatureDefaults:ValueLengthLimit"], out var vl) ? vl : 1_048_576;
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = multipartLimit;
            options.ValueLengthLimit = valueLimit;
        });

        return services;
    }
}
