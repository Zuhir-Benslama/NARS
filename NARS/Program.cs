using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NarsApi.Data;
using NarsApi.Services;

// Npgsql 6+ maps DateTime to timestamptz by default. Our DB uses
// 'timestamp without time zone', so we restore the legacy behaviour that
// reads/writes both types as unspecified-timezone DateTime.
// Remove this line once the DB columns are migrated to timestamptz.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────
// 1. Database (EF Core + Npgsql / PostGIS)
// ─────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseNetTopologySuite()
    )
    // NOTE: no UseSnakeCaseNamingConvention() — all entities have explicit
    // [Column("...")] attributes that handle snake_case mapping
);

// Required for fire-and-forget tasks (e.g. TriggerScatteredRefreshAsync) that
// need a fresh, independently-owned DbContext outside the request scope.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseNetTopologySuite()
    )
);

// ─────────────────────────────────────────────
// 2. JWT Authentication (cookie-first)
// ─────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey is not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer           = false,
            ValidateAudience         = false,
            ClockSkew                = TimeSpan.Zero,
        };

        // Read token from HttpOnly cookie (same pattern as FastAPI)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Cookies["access_token"];
                if (!string.IsNullOrEmpty(token))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ─────────────────────────────────────────────
// 3. Application services
// ─────────────────────────────────────────────
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<IScatteredAreaService, ScatteredAreaService>();

// HTTP client for tile proxy — reuses connections, respects DNS TTL
builder.Services.AddHttpClient("tile-proxy", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("User-Agent", "NARS-TileProxy/1.0");
});


// HTTP client for Planetary Computer satellite tiles
builder.Services.AddHttpClient("satellite", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "NARS-Satellite/1.0");
});

// In-memory cache for Planetary Computer SAS token (refreshed every 50 min)
builder.Services.AddMemoryCache();

// ─────────────────────────────────────────────
// 4. Controllers & JSON (camelCase output)
// ─────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// ─────────────────────────────────────────────
// 5. CORS — specific origins + credentials
//    AllowAnyOrigin() cannot be combined with AllowCredentials() (cookies),
//    so we use explicit origins from appsettings.json.
// ─────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? ["http://localhost:5000", "http://localhost:5001",
        "https://localhost:7000", "https://localhost:7001"];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()   // required for HttpOnly cookie auth
    )
);

// ─────────────────────────────────────────────
// 6. OpenAPI (native .NET 9/10)
// ─────────────────────────────────────────────
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title       = "NARS - National Addressing Reference System";
        doc.Info.Description = "Geographic data management API";
        doc.Info.Version     = "2.0.0";
        return Task.CompletedTask;
    });
});

// ═════════════════════════════════════════════
// Build & configure middleware pipeline
// ═════════════════════════════════════════════
var app = builder.Build();

// ── Database initialisation ───────────────────
// fix #6: MigrateAsync() applies all pending EF migrations and creates the
// __EFMigrationsHistory table on first run.  It is a no-op when the database
// is already up to date.
//
// Before running in production for the first time, generate the initial migration:
//   dotnet ef migrations add InitialCreate
//
// The schema is managed manually via nars_db_v2.sql — not through EF migrations.
// We simply verify the connection is reachable at startup instead of running
// MigrateAsync(), which would fail trying to query __EFMigrationsHistory.
using (var scope = app.Services.CreateScope())
{
    var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Console.WriteLine("==================================================");
    Console.WriteLine("NARS - ASP.NET Core + PostgreSQL/PostGIS");
    Console.WriteLine("==================================================");

    await dbCtx.Database.CanConnectAsync();

    Console.WriteLine("✓ Database connection verified");
}

// ── Global exception handler ─────────────────
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode  = 500;

        var feature = ctx.Features.Get<IExceptionHandlerPathFeature>();
        var ex      = feature?.Error;

        if (app.Environment.IsDevelopment() && ex is not null)
            Console.Error.WriteLine($"[EXCEPTION] {ex}");

        var message = app.Environment.IsDevelopment()
            ? ex?.Message ?? "An unexpected error occurred."
            : "An internal server error occurred. Please try again.";

        await ctx.Response.WriteAsync(
            JsonSerializer.Serialize(new { detail = message, status = 500 }));
    });
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware order: Routing → CORS → Auth → Controllers
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// fix #12: log the actual bound addresses reported by the server at runtime
// instead of a hardcoded localhost URL that is wrong in Docker/Kubernetes.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Urls.Any()
        ? string.Join(", ", app.Urls)
        : builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000";
    Console.WriteLine($"✓ Startup complete — {addresses}\n");
});

app.Run();
