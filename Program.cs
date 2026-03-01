using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NarsApi.Data;
using NarsApi.Services;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────
// 1. Database (EF Core + Npgsql / PostGIS)
// ─────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options
        .UseNpgsql(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            o => o.UseNetTopologySuite()   // PostGIS spatial support
        )
        // NOTE: no UseSnakeCaseNamingConvention() — all entities already have
        // explicit [Column("...")] attributes that handle the snake_case mapping
);

// ─────────────────────────────────────────────
// 2. JWT Authentication
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

        // Also accept token from cookie (cookie-first auth, same as FastAPI)
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
// 3. Application Services
// ─────────────────────────────────────────────
builder.Services.AddScoped<JwtService>();

// ─────────────────────────────────────────────
// 4. Controllers & JSON
// ─────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Use camelCase in responses, matching FastAPI's default output
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// ─────────────────────────────────────────────
// 5. CORS — must use specific origins when AllowCredentials() is needed,
//    because AllowAnyOrigin() + AllowCredentials() is forbidden by the
//    browser (it sends Access-Control-Allow-Origin: * which makes the
//    browser refuse to include cookies, causing silent 401s on every save).
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
              .AllowCredentials()          // required for cookie-based auth
    )
);

// ─────────────────────────────────────────────
// 6. OpenAPI (native .NET 10 — replaces Swashbuckle)
// ─────────────────────────────────────────────
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title       = "NARS - National Addressing Reference System";
        doc.Info.Description = "Geographic data management API — ASP.NET Core port of the FastAPI original";
        doc.Info.Version     = "2.0.0";
        return Task.CompletedTask;
    });
});

// ─────────────────────────────────────────────
// Build & Configure Middleware Pipeline
// ─────────────────────────────────────────────
var app = builder.Build();

// Ensure tables exist on startup (mirrors FastAPI lifespan startup)
using (var scope = app.Services.CreateScope())
{
    var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Console.WriteLine("==================================================");
    Console.WriteLine("NARS - ASP.NET Core + PostgreSQL/PostGIS");
    Console.WriteLine("==================================================");
    await dbCtx.Database.EnsureCreatedAsync();
    Console.WriteLine("✓ Database tables ready");
}

if (app.Environment.IsDevelopment())
{
    // Serves the OpenAPI JSON at /openapi/v1.json
    app.MapOpenApi();
}

// BUG FIX: Global JSON exception handler.
// Without this, any unhandled exception returns an HTML page or empty 500
// body — the frontend cannot parse it, so the popup shows no useful detail.
// This ensures all unhandled exceptions return { "detail": "...", "status": 500 }.
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode  = 500;

        var feature = ctx.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var ex = feature?.Error;

        var message = app.Environment.IsDevelopment()
            ? ex?.Message ?? "An unexpected error occurred."
            : "An internal server error occurred. Please try again.";

        if (app.Environment.IsDevelopment() && ex is not null)
            Console.Error.WriteLine($"[EXCEPTION] {ex}");

        var json = System.Text.Json.JsonSerializer.Serialize(
            new { detail = message, status = 500 });
        await ctx.Response.WriteAsync(json);
    });
});

// Static files from wwwroot/
app.UseStaticFiles();

// UseRouting must precede UseCors for endpoint-based CORS to apply correctly
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine($"✓ Startup complete — http://localhost:5000\n");

app.Run();
