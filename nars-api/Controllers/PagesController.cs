using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Serves the HTML pages. Static assets (app.js, app.css) are handled
/// automatically by UseStaticFiles() from wwwroot/ — no explicit routes needed.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
#pragma warning disable S6931 // PagesController serves root-level paths (/, /login, /map), not API endpoints
public class PagesController(
    IJwtService jwt,
    IAntiforgery antiforgery,
    IMemoryCache cache,
    IHostEnvironment env,
    ILogger<PagesController> logger,
    IRefreshTokenService refreshService,
    IOptions<CacheOptions> cacheOptions,
    IDateTimeProvider timeProvider,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    // GET / — redirect to map if authenticated, otherwise to login
    [HttpGet("/")]
    [AllowAnonymous]
    public async Task<IActionResult> Root(CancellationToken cancellationToken = default)
    {
        if (await TryAuthenticateAsync(cancellationToken))
        {
            return Redirect("/map");
        }

        return Redirect("/login");
    }

    // GET /login — inject CSRF token and CSP nonce into the HTML
    [HttpGet("/login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginPage()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var nonce = HttpContext.Items["csp-nonce"] as string ?? string.Empty;

        var template = (await LoadPageTemplateAsync("login_html", "login.html")) ?? string.Empty;

        var html = template
            // Inject the CSRF token into the <meta name="csrf-token"> placeholder
            .Replace("<meta name=\"csrf-token\" content=\"\">",
                $"<meta name=\"csrf-token\" content=\"{HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty)}\">")
            // Inject nonce into every <script> tag so they pass the CSP check
            .Replace("<script>", $"<script nonce=\"{nonce}\">");

        return Content(html, "text/html");
    }

    // GET /map — auth-guarded
    // Reads index.html as a template and injects the CSRF token + CSP nonce so
    // the SPA can send authenticated state-mutating requests without CSRF errors.
    [HttpGet("/map")]
    [AllowAnonymous]
    public async Task<IActionResult> MapPage(CancellationToken cancellationToken = default)
    {
        if (!await TryAuthenticateAsync(cancellationToken))
        {
            logger.LogDebug("[Pages] Map page not authenticated, redirecting to /login");
            return Redirect("/login");
        }

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var nonce = HttpContext.Items["csp-nonce"] as string ?? string.Empty;

        var template = (await LoadPageTemplateAsync("index_html", "index.html")) ?? string.Empty;

        var html = template
            // Inject CSRF token into the meta placeholder
            .Replace("<meta name=\"csrf-token\" content=\"\">",
                $"<meta name=\"csrf-token\" content=\"{HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty)}\">")
            // Inject nonce into every <script> tag so they pass the CSP check
            .Replace("<script ", $"<script nonce=\"{nonce}\" ");

        return Content(html, "text/html");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> LoadPageTemplateAsync(string cacheKey, string fileName)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fileName);
        if (env.IsDevelopment())
        {
            return await System.IO.File.ReadAllTextAsync(path);
        }

        return (await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(cacheOptions.Value.PageTemplateDurationHours);
            return await System.IO.File.ReadAllTextAsync(path);
        })) ?? string.Empty;
    }

    private async Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        var principal = ValidateAccessTokenFromCookie();
        principal ??= ValidateAccessTokenFromBearerHeader();

        if (principal is not null)
        {
            await HttpContext.SignInAsync("Pages", principal);
            return true;
        }

        return await TryRefreshSessionAsync(cancellationToken);
    }

    private ClaimsPrincipal? ValidateAccessTokenFromCookie()
    {
        var accessToken = Request.Cookies["access_token"];
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        var principal = jwt.ValidateToken(accessToken);
        if (principal is not null)
        {
            logger.LogDebug("[Pages] access_token cookie is valid.");
        }
        else
        {
            logger.LogDebug("[Pages] access_token cookie is EXPIRED or INVALID.");
        }

        return principal;
    }

    private ClaimsPrincipal? ValidateAccessTokenFromBearerHeader()
    {
        var bearerHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(bearerHeader)
            || !bearerHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bearerToken = bearerHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(bearerToken))
        {
            return null;
        }

        var principal = jwt.ValidateToken(bearerToken);
        if (principal is not null)
        {
            logger.LogDebug("[Pages] Valid bearer token header found. Setting access_token cookie.");
            Response.Cookies.Append("access_token", bearerToken, MakeCookieOptions(jwt.AccessTokenExpiresIn));
        }
        else
        {
            logger.LogDebug("[Pages] Bearer token header is invalid or expired.");
        }

        return principal;
    }

    private async Task<bool> TryRefreshSessionAsync(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogDebug("[Pages] refresh_token cookie NOT FOUND. Cannot silent refresh.");
            return false;
        }

        logger.LogDebug("[Pages] Found refresh_token. Attempting silent refresh...");

        try
        {
            var result = await refreshService.RotateRefreshTokenAsync(refreshToken, cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("[Pages] Refresh failed: {Detail}", result.Detail);
                return false;
            }

            if (result.RefreshExpiry is null || result.NewAccessToken is null || result.NewRawToken is null)
            {
                logger.LogWarning("[Pages] Refresh succeeded but token data is missing.");
                return false;
            }

            var maxAge = result.RefreshExpiry.Value - timeProvider.UtcNow;
            logger.LogDebug("[Pages] Silent refresh SUCCESS. Issuing new cookies for {Username}", result.Username);
            Response.Cookies.Append("access_token", result.NewAccessToken, MakeCookieOptions(jwt.AccessTokenExpiresIn));
            Response.Cookies.Append("refresh_token", result.NewRawToken, MakeCookieOptions(maxAge));

            var principal = result.NewAccessToken is not null ? jwt.ValidateToken(result.NewAccessToken) : null;
            if (principal is not null)
            {
                await HttpContext.SignInAsync("Pages", principal);
            }

            return true;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "[Pages] Error during silent refresh");
            return false;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "[Pages] IO error during silent refresh");
            return false;
        }
    }

}
