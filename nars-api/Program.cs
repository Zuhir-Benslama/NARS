using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

// Npgsql 6+ maps DateTime to timestamptz by default. Our DB uses
// 'timestamp without time zone', so we restore the legacy behaviour that
// reads/writes both types as unspecified-timezone DateTime.
// Remove this line once the DB columns are migrated to timestamptz.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ═══════════════════════════════════════════════════════════════
// 0. Secrets — environment variables override appsettings.json.
//    NEVER commit real secrets to appsettings.json.
// ═══════════════════════════════════════════════════════════════
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");

// Read the DB password directly from the OS environment variable.
// builder.Configuration["NARS_DB_PASSWORD"] also works but only if the var
// is exported before the process starts. Environment.GetEnvironmentVariable
// is unambiguous and works regardless of shell export state.
var envDbPassword = Environment.GetEnvironmentVariable("NARS_DB_PASSWORD")
    ?? builder.Configuration["NARS_DB_PASSWORD"];

var hasPlaceholder = connStr?.Contains("${NARS_DB_PASSWORD}") == true;

if (hasPlaceholder && string.IsNullOrEmpty(envDbPassword))
{
    throw new InvalidOperationException(
        "Database password is not configured. " +
        "Set the NARS_DB_PASSWORD environment variable before starting the server.");
}

if (!string.IsNullOrEmpty(envDbPassword))
    connStr = connStr?.Replace("${NARS_DB_PASSWORD}", envDbPassword);

var jwtSecret = (builder.Configuration["NARS_JWT_SECRET"]
    ?? builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey is not configured. Set NARS_JWT_SECRET env var or Jwt:SecretKey in appsettings.Production.json.")).Trim();

if (jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters for HMAC-SHA256 security.");

// ═══════════════════════════════════════════════════════════════
// 0.5 OpenTelemetry — traces, metrics, logs via OTLP
// ═══════════════════════════════════════════════════════════════
var otelEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
    ?? "http://otel-collector.observability:4317";

builder.Services.AddOpenTelemetry()
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

// ═══════════════════════════════════════════════════════════════
// 1. Database (EF Core + Npgsql / PostGIS)
// ═══════════════════════════════════════════════════════════════
if (string.IsNullOrEmpty(connStr))
    throw new InvalidOperationException("DefaultConnection is not configured.");

builder.Services.AddNarsDatabase(connStr);

// ═══════════════════════════════════════════════════════════════
// 2. JWT Authentication (cookie-first)
// ═══════════════════════════════════════════════════════════════
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

// Validate issuer/audience in production — defense-in-depth against token forgery.
if (builder.Environment.IsProduction() && (string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience)))
{
    throw new InvalidOperationException(
        "Jwt:Issuer and Jwt:Audience must be configured in production for defense-in-depth. " +
        "Set them in appsettings.Production.json or via environment variables.");
}

// Logged after build when logger is available
var logJwtWarning = string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience);

builder.Services.AddNarsJwtAuthentication(
    jwtSecret,
    issuer: jwtIssuer,
    audience: jwtAudience);

// ═══════════════════════════════════════════════════════════════
// 3. Application services
// ═══════════════════════════════════════════════════════════════
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<BackgroundQueueProcessor>();
builder.Services.AddScoped<IScatteredAreaService, ScatteredAreaService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IValidationService, ValidationService>();
builder.Services.AddScoped<IFieldService, FieldService>();
builder.Services.AddScoped<IFeatureStatsService, FeatureStatsService>();
builder.Services.AddScoped<IBoundaryService, BoundaryService>();
builder.Services.AddScoped<IEntranceQueryService, EntranceQueryService>();
builder.Services.AddScoped<IAdminOverviewService, AdminOverviewService>();

// HTTP client for tile proxy
var tileTimeout = int.TryParse(builder.Configuration["HttpClient:TileProxyTimeoutSeconds"], out var tts) ? tts : 15;
builder.Services.AddHttpClient("tile-proxy", client =>
{
    client.Timeout = TimeSpan.FromSeconds(tileTimeout);
    client.DefaultRequestHeaders.Add("User-Agent", "NARS-TileProxy/1.0");
});

// HTTP client for Planetary Computer satellite tiles
var satTimeout = int.TryParse(builder.Configuration["HttpClient:SatelliteTimeoutSeconds"], out var sts) ? sts : 30;
builder.Services.AddHttpClient("satellite", client =>
{
    client.Timeout = TimeSpan.FromSeconds(satTimeout);
    client.DefaultRequestHeaders.Add("User-Agent", "NARS-Satellite/1.0");
});

// In-memory cache for Planetary Computer SAS token
builder.Services.AddMemoryCache();

// ═══════════════════════════════════════════════════════════════
// 4. Controllers & JSON (camelCase output)
// ═══════════════════════════════════════════════════════════════
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// ═══════════════════════════════════════════════════════════════
// 5. CORS + Compression
// ═══════════════════════════════════════════════════════════════
builder.Services.AddNarsCors(builder.Configuration);
builder.Services.AddNarsCompression();

// ═══════════════════════════════════════════════════════════════
// 6. OpenAPI (native .NET 10)
// ═══════════════════════════════════════════════════════════════
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title = "NARS - National Addressing Reference System";
        doc.Info.Description = "Geographic data management API";
        doc.Info.Version = "2.0.0";
        return Task.CompletedTask;
    });
});

// ═══════════════════════════════════════════════════════════════
// 7. Rate limiting — values from appsettings.json
// ═══════════════════════════════════════════════════════════════
builder.Services.AddNarsRateLimiting(builder.Configuration);

// ═══════════════════════════════════════════════════════════════
// 8. Health checks — database connectivity for K8s probes
// ═══════════════════════════════════════════════════════════════
builder.Services.AddHealthChecks()
    .AddNarsDatabaseHealthCheck(connStr);

// ═══════════════════════════════════════════════════════════════
// 9. Antiforgery (CSRF protection)
// ═══════════════════════════════════════════════════════════════
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-Token";
    options.Cookie.Name = "X-CSRF-TOKEN-COOKIE";
    options.Cookie.HttpOnly = false;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ═══════════════════════════════════════════════════════════════
// 10. Request body size limits
// ═══════════════════════════════════════════════════════════════
var multipartLimit = int.TryParse(builder.Configuration["FeatureDefaults:MultipartBodyLengthLimit"], out var mll) ? mll : 10_485_760;
var valueLimit = int.TryParse(builder.Configuration["FeatureDefaults:ValueLengthLimit"], out var vl) ? vl : 1_048_576;
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = multipartLimit;
    options.ValueLengthLimit = valueLimit;
});

// ═══════════════════════════════════════════════════════════════
// Build & configure middleware pipeline
// ═══════════════════════════════════════════════════════════════
var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

if (logJwtWarning)
    startupLogger.LogWarning("JWT Issuer/Audience validation is disabled. Set Jwt:Issuer and Jwt:Audience for defense-in-depth.");

// ── Database initialisation ───────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    startupLogger.LogInformation("==================================================");
    startupLogger.LogInformation("NARS - ASP.NET Core + PostgreSQL/PostGIS");
    startupLogger.LogInformation("==================================================");

    await dbCtx.Database.CanConnectAsync();
    startupLogger.LogInformation("Database connection verified");

    await dbCtx.Database.MigrateAsync();
    startupLogger.LogInformation("Database migrations applied");
}

// ── Global exception handler ──────────────────────────────────
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = 500;

        var feature = ctx.Features.Get<IExceptionHandlerPathFeature>();
        var ex = feature?.Error;

        if (ex is not null)
        {
            var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Unhandled exception");
        }

        var message = app.Environment.IsDevelopment()
            ? ex?.Message ?? "An unexpected error occurred."
            : "An internal server error occurred. Please try again.";

        await ctx.Response.WriteAsync(
            JsonSerializer.Serialize(new { detail = message, status = 500 }));
    });
});

app.UseDefaultFiles();

// Explicitly register MIME types so Content-Type is always set.
// Without this, the default FileExtensionContentTypeProvider maps .js to the
// deprecated "application/javascript". When combined with X-Content-Type-Options: nosniff,
// any missing or incorrect Content-Type causes browsers to block the resource entirely.
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".js"] = "text/javascript";    // RFC 9239 (supersedes application/javascript)
contentTypeProvider.Mappings[".mjs"] = "text/javascript";
contentTypeProvider.Mappings[".css"] = "text/css";
contentTypeProvider.Mappings[".woff2"] = "font/woff2";
contentTypeProvider.Mappings[".woff"] = "font/woff";
contentTypeProvider.Mappings[".ico"] = "image/x-icon";
contentTypeProvider.Mappings[".svg"] = "image/svg+xml";
contentTypeProvider.Mappings[".map"] = "application/json";   // source maps

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        // HTML shells are not content-addressed — always revalidate.
        if (name is "index.html" or "login.html")
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate");
            ctx.Context.Response.Headers.Append("Pragma", "no-cache");
            return;
        }
        // Vite-hashed assets (e.g. index-C3VuOP09.js) are content-addressed.
        // Cache them indefinitely — the hash changes whenever content changes.
        if (name.EndsWith(".js") || name.EndsWith(".mjs") || name.EndsWith(".css") || name.EndsWith(".woff2"))
        {
            ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
        }
    }
});

// Middleware order: Routing → ForwardedHeaders → CORS → Compression → RateLimit → Auth → Antiforgery → Controllers
app.UseRouting();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors();
app.UseResponseCompression();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Security headers middleware — CSP with per-request nonce, X-Content-Type-Options, etc.
app.Use(async (ctx, next) =>
{
    // Only set security headers on non-API responses (API is JSON, not HTML)
    if (!ctx.Request.Path.StartsWithSegments("/api") && !ctx.Response.HasStarted)
    {
        // Generate a per-request nonce for script-src.
        // PagesController reads this same value from HttpContext.Items to inject
        // it into the <script nonce=""> attribute of the served HTML.
        var nonceBytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);
        ctx.Items["csp-nonce"] = nonce;

        // Content-Security-Policy: 'unsafe-inline' removed from script-src.
        // 'nonce-{value}' allows only scripts with the matching nonce attribute.
        // 'unsafe-inline' is still required on style-src for maplibre/geoman dynamic styles.
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            $"script-src 'self' 'nonce-{nonce}' blob:; " +
            "worker-src 'self' blob:; " +
            "style-src 'self' https://cdn.jsdelivr.net https://unpkg.com 'unsafe-inline' https://fonts.googleapis.com; " +
            "img-src 'self' data: blob: https://*.tile.openstreetmap.org https://*.basemaps.cartocdn.com https://*.arcgisonline.com; " +
            "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
            "connect-src 'self' http: https: data: ws://127.0.0.1:* http://127.0.0.1:* https://*.arcgisonline.com https://*.tile.openstreetmap.org https://*.basemaps.cartocdn.com; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'";

        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
    }
    await next();
});

// CSRF middleware — covers server-rendered form submissions (login.html).
// /api endpoints are exempt: auth cookies use SameSite=Lax, so browsers
// will not attach them on cross-origin state-mutating requests, which is
// equivalent CSRF protection without requiring a token header.
app.Use(async (ctx, next) =>
{
    var method = ctx.Request.Method.ToUpperInvariant();
    var isAuthenticated = ctx.User.Identity?.IsAuthenticated == true;
    var isApiPath = ctx.Request.Path.StartsWithSegments("/api");
    if (method is not ("GET" or "HEAD" or "OPTIONS" or "TRACE")
        && isAuthenticated
        && !isApiPath)
    {
        var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        try { await antiforgery.ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { detail = "CSRF validation failed." });
            return;
        }
    }
    await next();
});

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTheme(ScalarTheme.Purple);
});

app.MapControllers();

// Health check endpoint — includes database connectivity check
app.MapHealthChecks("/health");
app.MapHealthChecks("/api/health");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Urls.Any()
        ? string.Join(", ", app.Urls)
        : builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000";
    startupLogger.LogInformation("Startup complete — {Addresses}", addresses);
});

await app.RunAsync();
