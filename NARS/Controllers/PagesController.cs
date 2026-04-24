using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NarsApi.Data;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Serves the HTML pages. Static assets (app.js, app.css) are handled
/// automatically by UseStaticFiles() from wwwroot/ — no explicit routes needed.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class PagesController(
    AppDbContext db,
    JwtService jwt,
    IAntiforgery antiforgery,
    IMemoryCache cache,
    IHostEnvironment env,
    IConfiguration config,
    ILogger<PagesController> logger) : ControllerBase
{
    // GET / — redirect to map if authenticated, otherwise to login
    [HttpGet("/")]
    public async Task<IActionResult> Root()
    {
        logger.LogInformation("[Pages] Root request. Checking auth...");
        if (await IsAuthenticatedAsync())
        {
            logger.LogInformation("[Pages] Root authenticated, redirecting to /map");
            return Redirect("/map");
        }

        logger.LogInformation("[Pages] Root NOT authenticated, redirecting to /login");
        return Redirect("/login");
    }

    // GET /login — inject CSRF token and CSP nonce into the HTML
    [HttpGet("/login")]
    public IActionResult LoginPage()
    {
        logger.LogInformation("[Pages] Serving login page");
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var nonce = HttpContext.Items["csp-nonce"] as string ?? string.Empty;

        var template = LoadPageTemplate("login_html", "login.html");

        var html = template!
            // Inject the CSRF token into the <meta name="csrf-token"> placeholder
            .Replace("<meta name=\"csrf-token\" content=\"\">",
                $"<meta name=\"csrf-token\" content=\"{tokens.RequestToken}\">")
            // Inject nonce into every <script> tag so they pass the CSP check
            .Replace("<script>", $"<script nonce=\"{nonce}\">");

        return Content(html, "text/html");
    }

    // GET /map — auth-guarded
    // Reads index.html as a template and injects the CSRF token + CSP nonce so
    // the SPA can send authenticated state-mutating requests without CSRF errors.
    [HttpGet("/map")]
    public async Task<IActionResult> MapPage()
    {
        logger.LogInformation("[Pages] Map page request. Checking auth...");
        if (!await IsAuthenticatedAsync())
        {
            logger.LogWarning("[Pages] Map page NOT authenticated.");
            // Fallback: check URL query parameter for token (dev workaround)
            var urlToken = Request.Query["token"].FirstOrDefault();
            if (!string.IsNullOrEmpty(urlToken))
            {
                logger.LogInformation("[Pages] Found token in query string. Validating...");
                if (jwt.ValidateToken(urlToken) is not null)
                {
                    logger.LogInformation("[Pages] Query token valid. Setting cookie.");
                    // Set token cookie for future requests
                    Response.Cookies.Append("access_token", urlToken, MakeCookieOptions(TimeSpan.FromHours(24)));
                }
                else
                {
                    logger.LogWarning("[Pages] Query token INVALID. Redirecting to /login");
                    return Redirect("/login");
                }
            }
            else
            {
                logger.LogWarning("[Pages] No query token. Redirecting to /login");
                return Redirect("/login");
            }
        }

        logger.LogInformation("[Pages] Serving index.html for map");

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var nonce = HttpContext.Items["csp-nonce"] as string ?? string.Empty;

        var template = LoadPageTemplate("index_html", "index.html");

        var html = template!
            // Inject CSRF token into the meta placeholder
            .Replace("<meta name=\"csrf-token\" content=\"\">",
                $"<meta name=\"csrf-token\" content=\"{tokens.RequestToken}\">")
            // Inject nonce into every <script> tag so they pass the CSP check
            .Replace("<script ", $"<script nonce=\"{nonce}\" ");

        return Content(html, "text/html");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string LoadPageTemplate(string cacheKey, string fileName)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fileName);
        // In development, avoid template caching so HTML updates are reflected immediately.
        if (env.IsDevelopment())
            return System.IO.File.ReadAllText(path);

        return cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return System.IO.File.ReadAllText(path);
        }) ?? string.Empty;
    }

    private async Task<bool> IsAuthenticatedAsync()
    {
        // Respect the principal populated by UseAuthentication() first.
        if (User.Identity?.IsAuthenticated == true)
        {
            logger.LogDebug("[Pages] HttpContext.User is already authenticated.");
            return true;
        }

        var accessToken = Request.Cookies["access_token"];
        if (!string.IsNullOrEmpty(accessToken))
        {
            logger.LogDebug("[Pages] Found access_token cookie. Validating...");
            if (jwt.ValidateToken(accessToken) is not null)
            {
                logger.LogDebug("[Pages] access_token is valid.");
                return true;
            }
            logger.LogInformation("[Pages] access_token is EXPIRED or INVALID.");
        }
        else
        {
            logger.LogInformation("[Pages] access_token cookie NOT FOUND.");
        }

        // Support authenticated clients that send a bearer token header.
        var bearerHeader = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(bearerHeader)
            && bearerHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearerToken = bearerHeader["Bearer ".Length..].Trim();
            if (!string.IsNullOrEmpty(bearerToken) && jwt.ValidateToken(bearerToken) is not null)
            {
                logger.LogInformation("[Pages] Valid bearer token header found. Setting access_token cookie.");
                Response.Cookies.Append("access_token", bearerToken, MakeCookieOptions(TimeSpan.FromHours(24)));
                return true;
            }

            logger.LogInformation("[Pages] Bearer token header is invalid or expired.");
        }

        // Access token missing or expired — try silent refresh via refresh_token
        return await TryRefreshSessionAsync();
    }

    private async Task<bool> TryRefreshSessionAsync()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogInformation("[Pages] refresh_token cookie NOT FOUND. Cannot silent refresh.");
            return false;
        }

        logger.LogInformation("[Pages] Found refresh_token. Attempting silent refresh...");
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(refreshToken)));

        // Row-level lock to prevent race conditions during refresh rotation
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var stored = await db.RefreshTokens
                .FromSqlRaw(
                    "SELECT * FROM refresh_tokens WHERE token_hash = {0} AND revoked = false AND expires_at > NOW() ORDER BY created_at DESC LIMIT 1 FOR UPDATE SKIP LOCKED",
                    hash)
                .SingleOrDefaultAsync();

            if (stored is null)
            {
                logger.LogWarning("[Pages] refresh_token hash NOT FOUND or EXPIRED in DB.");
                await tx.RollbackAsync();
                return false;
            }

            var user = await db.Users.FindAsync(stored.UserId);
            if (user is null)
            {
                logger.LogWarning("[Pages] User for refresh_token NOT FOUND.");
                await tx.RollbackAsync();
                return false;
            }

            // Rotate: revoke old, issue new
            stored.Revoked = true;
            var (newRaw, newHash) = JwtService.CreateRefreshToken();
            var refreshDays = int.TryParse(config["Jwt:RefreshExpiresInDays"], out var d) ? d : 30;
            var refreshExpiry = DateTime.UtcNow.AddDays(refreshDays);

            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = newHash,
                ExpiresAt = refreshExpiry,
            });

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            var newToken = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email,
                communeId: user.CommuneId, role: user.Role, dairaId: user.DairaId, wilayaId: user.WilayaId);
            var maxAge = refreshExpiry - DateTime.UtcNow;

            logger.LogInformation("[Pages] Silent refresh SUCCESS. Issuing new cookies for {Username}", user.Username);
            Response.Cookies.Append("access_token", newToken, MakeCookieOptions(TimeSpan.FromHours(24)));
            Response.Cookies.Append("refresh_token", newRaw, MakeCookieOptions(maxAge));

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Pages] Error during silent refresh");
            await tx.RollbackAsync();
            return false;
        }
    }

    private CookieOptions MakeCookieOptions(TimeSpan maxAge)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = maxAge,
            Path = "/",
            IsEssential = true,
        };
    }
}
