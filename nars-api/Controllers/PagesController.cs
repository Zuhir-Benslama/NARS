using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NarsApi.Infrastructure;
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
    IAntiforgery antiforgery,
    IMemoryCache cache,
    IHostEnvironment env,
    ILogger<PagesController> logger,
    IPageAuthService pageAuth,
    IOptions<CacheOptions> cacheOptions,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    // GET / — redirect to map if authenticated, otherwise to login
    [HttpGet("/")]
    [AllowAnonymous]
    public async Task<IActionResult> Root(CancellationToken cancellationToken = default)
    {
        if (await pageAuth.TryAuthenticateAsync(cancellationToken))
        {
            return Redirect("/map");
        }

        return Redirect("/login");
    }

    // GET /login — inject CSRF token and CSP nonce into the HTML
    [HttpGet("/login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginPage(CancellationToken cancellationToken = default)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var nonce = HttpContext.Items["csp-nonce"] as string ?? string.Empty;

        var template = (await LoadPageTemplateAsync("login_html", "login.html", cancellationToken)) ?? string.Empty;

        var html = template
            // Inject the CSRF token into the <meta name="csrf-token"> placeholder
            .Replace("<meta name=\"csrf-token\" content=\"\">",
                $"<meta name=\"csrf-token\" content=\"{HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty)}\">")
            // Inject nonce into every <script> tag so they pass the CSP check.
            // Order matters: replace <script ... (with attributes) first so the
            // bare <script> replacement doesn't create a double-nonce match.
            .Replace("<script ", $"<script nonce=\"{nonce}\" ")
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
        if (!await pageAuth.TryAuthenticateAsync(cancellationToken))
        {
            logger.LogDebug("[Pages] Map page not authenticated, redirecting to /login");
            return Redirect("/login");
        }

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var nonce = HttpContext.Items["csp-nonce"] as string ?? string.Empty;

        var template = (await LoadPageTemplateAsync("index_html", "index.html", cancellationToken)) ?? string.Empty;

        var html = template
            // Inject CSRF token into the meta placeholder
            .Replace("<meta name=\"csrf-token\" content=\"\">",
                $"<meta name=\"csrf-token\" content=\"{HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty)}\">")
            // Inject nonce into every <script> tag so they pass the CSP check.
            // Order matters: replace <script ... (with attributes) first so the
            // bare <script> replacement doesn't create a double-nonce match.
            .Replace("<script ", $"<script nonce=\"{nonce}\" ")
            .Replace("<script>", $"<script nonce=\"{nonce}\">");

        return Content(html, "text/html");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> LoadPageTemplateAsync(string cacheKey, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fileName);
        if (env.IsDevelopment())
        {
            return await System.IO.File.ReadAllTextAsync(path, cancellationToken);
        }

        return (await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(cacheOptions.Value.PageTemplateDurationHours);
            // Do not pass the request's token: a disconnect during the first load
            // must not abort the read and defeat caching for all later requests.
            return await System.IO.File.ReadAllTextAsync(path, CancellationToken.None);
        })) ?? string.Empty;
    }
}
