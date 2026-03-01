using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NarsApi.Data;
using NarsApi.Services;

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

// ── Ensure DB tables exist on startup ────────
using (var scope = app.Services.CreateScope())
{
    var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Console.WriteLine("==================================================");
    Console.WriteLine("NARS - ASP.NET Core + PostgreSQL/PostGIS");
    Console.WriteLine("==================================================");
    await dbCtx.Database.EnsureCreatedAsync();
    Console.WriteLine("✓ Database tables ready");
}

// ── Global exception handler ─────────────────
// Catches unhandled exceptions and returns structured JSON instead of
// an HTML error page or empty 500 body — so the frontend can display
// a meaningful error message.
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

// Static files from wwwroot/ (app.css, app.js, login.html served here)
app.UseStaticFiles();

// Middleware order: Routing → CORS → Auth → Controllers
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine($"✓ Startup complete — http://localhost:5000\n");

app.Run();
