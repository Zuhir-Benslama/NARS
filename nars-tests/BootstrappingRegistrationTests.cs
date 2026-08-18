using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Infrastructure;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Bootstrapping smoke tests: calling <see cref="ServiceRegistrationExtensions.AddNarsServices"/>
/// must register a coherent service graph that resolves without a database. These tests
/// exercise the registration code paths in ServiceRegistrationExtensions, DatabaseExtensions,
/// CorsCompressionExtensions, RateLimitExtensions, and the options binding in AppOptions.
/// </summary>
[Collection(ProgramStartupCollection.Name)]
public class BootstrappingRegistrationTests : IDisposable
{
    private const string TestConnStr = "Host=localhost;Port=5432;Database=test;Username=test;Password=test";

    // AddNarsOpenTelemetry reads OTEL_EXPORTER_OTLP_ENDPOINT from the environment at
    // registration time. An ambient value would either throw UriFormatException on a
    // malformed URI or spawn background OTLP exporter threads per provider, so these
    // tests pin a deterministic loopback endpoint and restore the original value.
    private readonly string? _savedOtelEndpoint =
        Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

    public void Dispose() =>
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", _savedOtelEndpoint);

    private static IConfiguration BuildConfig(Action<IConfigurationBuilder>? overrides = null)
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:ExpiresInMinutes"] = "60",
            ["Jwt:RefreshExpiresInDays"] = "30",
            ["RateLimit:AuthPermitLimit"] = "7",
            ["RateLimit:ApiPermitLimit"] = "100",
            ["RateLimit:ApiWindowMinutes"] = "2",
            ["RateLimit:ApiSegmentsPerWindow"] = "4",
            ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            ["HttpClient:TileProxyTimeoutSeconds"] = "20",
            ["Segmentation:BaseUrl"] = "http://localhost:8000",
        });
        overrides?.Invoke(builder);
        return builder.Build();
    }

    private static ServiceProvider BuildProvider(IConfiguration? config = null)
    {
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:4317");
        var services = new ServiceCollection();
        services.AddNarsServices(
            config ?? BuildConfig(),
            TestConnStr,
            AuthTestHelper.TestJwtSecret,
            jwtIssuer: "https://issuer.test",
            jwtAudience: "https://audience.test",
            Mock.Of<IHostEnvironment>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddNarsServices_RegistersAllDomainServices()
    {
        using var sp = BuildProvider();

        Assert.IsType<SystemDateTimeProvider>(sp.GetRequiredService<IDateTimeProvider>());
        Assert.IsType<BackgroundTaskQueue>(sp.GetRequiredService<IBackgroundTaskQueue>());
        Assert.IsType<FeatureService>(sp.GetRequiredService<IFeatureService>());
        Assert.IsType<FieldService>(sp.GetRequiredService<IFieldService>());
        Assert.IsType<ValidationService>(sp.GetRequiredService<IValidationService>());
        Assert.IsType<JwtService>(sp.GetRequiredService<IJwtService>());
        Assert.IsType<RefreshTokenService>(sp.GetRequiredService<IRefreshTokenService>());
        Assert.IsType<ScatteredAreaService>(sp.GetRequiredService<IScatteredAreaService>());
        Assert.IsType<UserAuthorizationService>(sp.GetRequiredService<IUserAuthorizationService>());
        Assert.IsType<BoundaryService>(sp.GetRequiredService<IBoundaryService>());
        Assert.IsType<FeatureStatsService>(sp.GetRequiredService<IFeatureStatsService>());
        Assert.IsType<LocationQueryService>(sp.GetRequiredService<ILocationQueryService>());
        Assert.IsType<LocationSearchService>(sp.GetRequiredService<ILocationSearchService>());
        Assert.IsType<AdminOverviewService>(sp.GetRequiredService<IAdminOverviewService>());
        Assert.IsType<EntranceQueryService>(sp.GetRequiredService<IEntranceQueryService>());
        Assert.IsType<RoadQueryService>(sp.GetRequiredService<IRoadQueryService>());
        Assert.IsType<UserProfileService>(sp.GetRequiredService<IUserProfileService>());
        Assert.IsType<UserCreationService>(sp.GetRequiredService<IUserCreationService>());
        Assert.IsType<CommuneScopeService>(sp.GetRequiredService<ICommuneScopeService>());
        Assert.IsType<DraftFeaturesService>(sp.GetRequiredService<IDraftFeaturesService>());
        Assert.IsType<ErrorLogService>(sp.GetRequiredService<IErrorLogService>());
    }

    [Fact]
    public async Task AddNarsServices_RegistersBackgroundHostedServices()
    {
        await using var sp = BuildProvider();

        Assert.Contains(sp.GetServices<IHostedService>(), s => s.GetType() == typeof(BackgroundQueueProcessor));
        Assert.Contains(sp.GetServices<IHostedService>(), s => s.GetType() == typeof(RefreshTokenPruner));
    }

    [Fact]
    public void AddNarsServices_BindsOptionsFromConfiguration()
    {
        using var sp = BuildProvider();

        var jwtOptions = sp.GetRequiredService<IOptions<JwtOptions>>().Value;
        Assert.Equal(60, jwtOptions.ExpiresInMinutes);
        Assert.Equal(30, jwtOptions.RefreshExpiresInDays);

        var cacheOptions = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
        Assert.Equal(1, cacheOptions.ReferenceDataDurationHours);

        Assert.Equal(TimeSpan.FromMinutes(60), sp.GetRequiredService<IJwtService>().AccessTokenExpiresIn);
    }

    [Fact]
    public void AddNarsServices_ConfiguresControllersJsonCamelCase()
    {
        using var sp = BuildProvider();

        var jsonOptions = sp.GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.Equal(JsonNamingPolicy.CamelCase, jsonOptions.JsonSerializerOptions.PropertyNamingPolicy);
    }

    [Fact]
    public async Task AddNarsServices_RegistersCorsPolicyWithExplicitOrigins()
    {
        using var sp = BuildProvider();
        var policyProvider = sp.GetRequiredService<ICorsPolicyProvider>();

        var policy = await policyProvider.GetPolicyAsync(new DefaultHttpContext(), policyName: null);

        Assert.NotNull(policy);
        Assert.Contains("http://localhost:3000", policy!.Origins);
        Assert.True(policy.SupportsCredentials);
        Assert.False(policy.AllowAnyOrigin);
    }

    [Fact]
    public void AddNarsServices_RegistersResponseCompressionWithGeoJsonMimeType()
    {
        using var sp = BuildProvider();

        var compressionOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.ResponseCompression.ResponseCompressionOptions>>().Value;
        Assert.True(compressionOptions.EnableForHttps);
        Assert.Contains(compressionOptions.MimeTypes, m => m == "application/geo+json");
    }

    [Fact]
    public void AddNarsServices_RegistersRateLimiterWithConfiguredPolicies()
    {
        using var sp = BuildProvider();
        var rateOptions = sp.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.Equal(StatusCodes.Status429TooManyRequests, rateOptions.RejectionStatusCode);
        Assert.NotNull(rateOptions.GlobalLimiter);

        using var lease = rateOptions.GlobalLimiter.AttemptAcquire(new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Loopback },
        });
        Assert.True(lease.IsAcquired);
    }

    [Fact]
    public void AddNarsServices_RegistersAntiforgeryWithCsrfHeader()
    {
        using var sp = BuildProvider();

        var antiforgery = sp.GetRequiredService<IAntiforgery>();
        Assert.NotNull(antiforgery);

        var options = sp.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal("X-CSRF-Token", options.HeaderName);
        Assert.Equal("X-CSRF-TOKEN-COOKIE", options.Cookie.Name);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void AddNarsServices_RegistersHealthCheckForDatabase()
    {
        using var sp = BuildProvider();

        var healthCheckService = sp.GetRequiredService<HealthCheckService>();
        Assert.NotNull(healthCheckService);

        var options = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        Assert.Contains(options.Registrations, r => r.Name == "database");
    }

    [Fact]
    public void AddNarsServices_RegistersFormOptionsFromFeatureDefaults()
    {
        using var sp = BuildProvider();

        var formOptions = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Features.FormOptions>>().Value;
        Assert.Equal(10_485_760, formOptions.MultipartBodyLengthLimit);
        Assert.Equal(1_048_576, formOptions.ValueLengthLimit);
    }

    [Fact]
    public void AddNarsServices_RegistersHttpClientsWithConfiguredTimeouts()
    {
        using var sp = BuildProvider();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

        using var tileProxy = httpClientFactory.CreateClient("tile-proxy");
        Assert.Equal(TimeSpan.FromSeconds(20), tileProxy.Timeout);

        using var satellite = httpClientFactory.CreateClient("satellite");
        Assert.Equal(TimeSpan.FromSeconds(30), satellite.Timeout);
    }

    [Fact]
    public async Task AddNarsServices_InvalidOptions_FailsStartupValidation()
    {
        // Cache:ReferenceDataDurationHours is [Range(1,168)] — 999 must fail on start.
        var config = BuildConfig(b => b.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Cache:ReferenceDataDurationHours"] = "999" }));

        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:4317");
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(s => s.AddNarsServices(config, TestConnStr, AuthTestHelper.TestJwtSecret, null, null, Mock.Of<IHostEnvironment>()))
            .Build();

        var ex = await Assert.ThrowsAsync<AggregateException>(() => host.StartAsync());
        Assert.Contains(ex.InnerExceptions,
            e => e is OptionsValidationException ove
                 && ove.Message.Contains("ReferenceDataDurationHours", StringComparison.Ordinal));
    }
}
