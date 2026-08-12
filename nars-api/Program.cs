using NarsApi.Infrastructure;

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

if (connStr is null)
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured. " +
        "Set it in appsettings.json or provide NARS_DB_PASSWORD via environment variable.");
}

var jwtSecret = GetRequiredConfig(builder.Configuration, "Jwt:SecretKey", ["NARS_JWT_SECRET", "Jwt:SecretKey"]);

const int minJwtSecretLength = 32;
if (jwtSecret.Length < minJwtSecretLength)
{
    throw new InvalidOperationException($"Jwt:SecretKey must be at least {minJwtSecretLength} characters for HMAC-SHA256 security.");
}

// Reject low-entropy secrets: a 64-char string of one repeated character passes
// the length check but is trivially guessable. Estimate Shannon entropy over the
// secret's characters and require roughly the entropy NIST SP 800-131A
// recommends for HMAC keys (≥ 112 bits).
const int minJwtSecretEntropyBits = 100;
if (EstimateShannonEntropy(jwtSecret) * jwtSecret.Length < minJwtSecretEntropyBits)
{
    throw new InvalidOperationException(
        $"Jwt:SecretKey does not have enough entropy (minimum {minJwtSecretEntropyBits} bits). " +
        "Generate a random key, e.g. `openssl rand -base64 32`.");
}

var signupToken = builder.Configuration["AdminSignup:SignupToken"];
var envSignupToken = Environment.GetEnvironmentVariable("NARS_ADMIN_SIGNUP_TOKEN");
if (!string.IsNullOrEmpty(envSignupToken))
{
    builder.Configuration["AdminSignup:SignupToken"] = envSignupToken;
    signupToken = envSignupToken;
}

if (string.IsNullOrWhiteSpace(signupToken) || signupToken.StartsWith("${"))
{
    throw new InvalidOperationException(
        "AdminSignup:SignupToken is not configured. " +
        "Set the NARS_ADMIN_SIGNUP_TOKEN environment variable or configure AdminSignup:SignupToken in appsettings.json.");
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
        {
            return val.Trim();
        }
    }

    throw new InvalidOperationException(
        $"{primaryKey} is not configured. Set one of: {string.Join(", ", fallbackKeys)} " +
        $"in appsettings.Production.json or via environment variables.");
}

/// <summary>
/// Estimates the Shannon entropy (bits per character) of a string from its
/// character frequency distribution. A uniformly distributed set of characters
/// scores high; a repeated single character scores zero.
/// </summary>
static double EstimateShannonEntropy(string value)
{
    var counts = new Dictionary<char, int>(value.Length);
    foreach (var c in value)
    {
        counts.TryGetValue(c, out var count);
        counts[c] = count + 1;
    }

    var entropy = 0.0;
    foreach (var count in counts.Values)
    {
        var p = (double)count / value.Length;
        entropy -= p * Math.Log2(p);
    }
    return entropy;
}

// Exposed for integration/contract tests via WebApplicationFactory.
public partial class Program
{
    private Program() { }
}
