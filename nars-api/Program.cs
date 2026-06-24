using NarsApi.Infrastructure;

// Npgsql 6+ maps DateTime to timestamptz by default. Our DB uses
// 'timestamp without time zone', so we restore the legacy behaviour that
// reads/writes both types as unspecified-timezone DateTime.
// Remove this line once the DB columns are migrated to timestamptz.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ── Secrets — env vars override appsettings.json ───────────────
var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
var envDbPassword = Environment.GetEnvironmentVariable("NARS_DB_PASSWORD")
    ?? builder.Configuration["NARS_DB_PASSWORD"];

if (connStr?.Contains("${NARS_DB_PASSWORD}") == true && string.IsNullOrEmpty(envDbPassword))
{
    throw new InvalidOperationException(
        "Database password is not configured. " +
        "Set the NARS_DB_PASSWORD environment variable before starting the server.");
}

if (!string.IsNullOrEmpty(envDbPassword))
{
    connStr = connStr?.Replace("${NARS_DB_PASSWORD}", envDbPassword);
}

var jwtSecret = GetRequiredConfig(builder.Configuration, "Jwt:SecretKey", ["NARS_JWT_SECRET", "Jwt:SecretKey"]);

if (jwtSecret.Length < 32)
{
    throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters for HMAC-SHA256 security.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (builder.Environment.IsProduction() && (string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience)))
{
    throw new InvalidOperationException(
        "Jwt:Issuer and Jwt:Audience must be configured in production for defense-in-depth. " +
        "Set them in appsettings.Production.json or via environment variables.");
}

var logJwtWarning = string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience);

// ── Register all services ─────────────────────────────────────
builder.Services.AddNarsServices(builder.Configuration, connStr!, jwtSecret, jwtIssuer, jwtAudience);

// ── Build & configure pipeline ────────────────────────────────
var app = builder.Build();
await app.ConfigureNarsPipelineAsync(builder.Configuration, logJwtWarning);
await app.RunAsync();

// ── Helpers ─────────────────────────────────────────────────────
static string GetRequiredConfig(IConfiguration config, string primaryKey, string[] fallbackKeys)
{
    foreach (var key in fallbackKeys)
    {
        var val = config[key];
        if (!string.IsNullOrWhiteSpace(val))
            return val.Trim();
    }

    throw new InvalidOperationException(
        $"{primaryKey} is not configured. Set one of: {string.Join(", ", fallbackKeys)} " +
        $"in appsettings.Production.json or via environment variables.");
}
