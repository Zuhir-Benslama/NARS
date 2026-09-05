using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using Scalar.AspNetCore;

namespace NarsApi.Infrastructure;

public static class PipelineExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<WebApplication> ConfigureNarsPipelineAsync(this WebApplication app, IConfiguration config, bool logJwtWarning)
    {
        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

        LogJwtWarning(startupLogger, logJwtWarning);
        await VerifyDatabaseConnectionAsync(app, startupLogger);
        UseExceptionHandling(app);
        // Security headers must run before static files so /index.html, /login.html
        // and the fingerprinted bundles get the same CSP/X-Frame-Options/nosniff
        // protection as the controller-served pages.
        UseSecurityMiddleware(app);
        UseStaticFilesWithCaching(app);
        UseCsrfValidation(app);
        UseApiEndpoints(app);
        LogStartupComplete(app, config, startupLogger);

        return app;
    }

    private static void LogJwtWarning(ILogger<Program> logger, bool logJwtWarning)
    {
        if (logJwtWarning)
        {
            logger.LogWarning("JWT Issuer/Audience validation is disabled. Set Jwt:Issuer and Jwt:Audience for defense-in-depth.");
        }
    }

    private static async Task VerifyDatabaseConnectionAsync(WebApplication app, ILogger<Program> logger)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        logger.LogInformation("==================================================");
        logger.LogInformation("NARS - ASP.NET Core + PostgreSQL/PostGIS");
        logger.LogInformation("==================================================");

        // Bound the startup connectivity probe: if the database host is
        // unreachable (packets dropped), Npgsql's connect timeout alone can
        // leave startup hanging for minutes. Fail fast instead.
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var canConnect = await dbCtx.Database.CanConnectAsync(connectTimeout.Token);
        if (!canConnect)
        {
            throw new InvalidOperationException(
                "Unable to connect to the database. " +
                "Verify the connection string and ensure the database server is running.");
        }

        logger.LogInformation("Database connection verified");
    }

    private static void UseExceptionHandling(WebApplication app)
        => app.UseExceptionHandler(errApp =>
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

                    if (ex is UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "Unauthorized access");
                        ctx.Response.StatusCode = 401;
                        var authProblem = new ProblemDetails
                        {
                            Detail = app.Environment.IsDevelopment() ? ex.Message : "Authentication required.",
                            Status = 401,
                            Title = "Unauthorized",
                        };
                        await ctx.Response.WriteAsJsonAsync(authProblem, JsonOptions);
                        return;
                    }

                    // Only ArgumentException reliably indicates malformed client input.
                    // InvalidOperationException (Sequence contains no elements, collection
                    // modified, EF tracking errors, ...) usually signals a server bug and
                    // must fall through to the 500 handler below.
                    if (ex is ArgumentException)
                    {
                        logger.LogWarning(ex, "Bad request: {Message}", ex.Message);
                        ctx.Response.StatusCode = 400;
                        var badRequestProblem = new ProblemDetails
                        {
                            Detail = app.Environment.IsDevelopment() ? ex.Message : "The request is invalid.",
                            Status = 400,
                            Title = "Bad Request",
                        };
                        await ctx.Response.WriteAsJsonAsync(badRequestProblem, JsonOptions);
                        return;
                    }

                    if (ex is KeyNotFoundException)
                    {
                        logger.LogWarning(ex, "Resource not found: {Message}", ex.Message);
                        ctx.Response.StatusCode = 404;
                        var notFoundProblem = new ProblemDetails
                        {
                            Detail = "The requested resource was not found.",
                            Status = 404,
                            Title = "Not Found",
                        };
                        await ctx.Response.WriteAsJsonAsync(notFoundProblem, JsonOptions);
                        return;
                    }

                    logger.LogError(ex, "Unhandled exception");
                }

                var message = app.Environment.IsDevelopment()
                    ? ex?.Message ?? "An unexpected error occurred."
                    : "An internal server error occurred. Please try again.";

                var problem = new ProblemDetails
                {
                    Detail = message,
                    Status = 500,
                    Title = "Internal Server Error",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                };
                await ctx.Response.WriteAsJsonAsync(problem, JsonOptions);
            });
        });

    private static void UseStaticFilesWithCaching(WebApplication app)
    {
        app.UseDefaultFiles();

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".mjs"] = "text/javascript";
        contentTypeProvider.Mappings[".woff2"] = "font/woff2";
        contentTypeProvider.Mappings[".map"] = "application/json";

        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypeProvider,
            OnPrepareResponse = ctx =>
            {
                var cacheControl = CacheControlForStaticAsset(ctx.File.Name);
                if (cacheControl.Length > 0)
                {
                    ctx.Context.Response.Headers.CacheControl = cacheControl;
                    if (cacheControl.StartsWith("no-store", StringComparison.Ordinal))
                    {
                        ctx.Context.Response.Headers.Pragma = "no-cache";
                    }
                }
            }
        });
    }

    // Vite content-fingerprints bundles as <name>-<8+char hash>.<ext>. Only these
    // files are safe to cache immutable for a year — a given hash never changes.
    private static readonly Regex ViteContentHashPattern = new(
        @"-[A-Za-z0-9_-]{8,}\.(?:js|mjs|css|woff2)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Computes the Cache-Control header for a static file. HTML is never cached;
    /// content-fingerprinted bundles (Vite hash in the filename) are cached
    /// immutable; any un-hashed asset is revalidated so a deploy is never stale.
    /// Returns an empty string when no header should be written.
    /// </summary>
    internal static string CacheControlForStaticAsset(string fileName)
    {
        if (fileName is "index.html" or "login.html")
        {
            return "no-store, no-cache, must-revalidate";
        }

        if (fileName.EndsWith(".js") || fileName.EndsWith(".mjs") || fileName.EndsWith(".css") || fileName.EndsWith(".woff2"))
        {
            return ViteContentHashPattern.IsMatch(fileName)
                ? "public, max-age=31536000, immutable"
                : "public, no-cache";
        }

        return string.Empty;
    }

    private static void UseSecurityMiddleware(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        var forwardingOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = app.Services.GetRequiredService<IOptions<ProxyOptions>>().Value.ForwardLimit,
        };
        foreach (var cidr in app.Services.GetRequiredService<IOptions<ProxyOptions>>().Value.KnownNetworks)
        {
            if (System.Net.IPNetwork.Parse(cidr) is { } network)
            {
                forwardingOptions.KnownIPNetworks.Add(network);
            }
        }
        app.UseForwardedHeaders(forwardingOptions);

        app.UseRouting();
        app.UseCors();
        app.UseResponseCompression();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        var cspOptions = app.Services.GetRequiredService<IOptions<CspOptions>>().Value;

        app.Use(async (ctx, next) =>
            await ApplyCspMiddlewareAsync(ctx, next, cspOptions));
    }

    /// <summary>
    /// Sets the per-request CSP nonce + security headers on non-API pages
    /// (<c>/login</c>, <c>/map</c>). The nonce is stashed in
    /// <c>ctx.Items["csp-nonce"]</c> so <see cref="NarsApi.Controllers.PagesController"/>
    /// can inject it into inline script tags, and embedded into script-src/style-src
    /// so <c>'unsafe-inline'</c> is never sent in production.
    /// API responses still get the nosniff header so a reflected or stored XSS
    /// payload can never be interpreted as a script by the browser.
    /// </summary>
    internal static async Task ApplyCspMiddlewareAsync(
        HttpContext ctx,
        RequestDelegate next,
        CspOptions cspOptions)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Headers.XContentTypeOptions = "nosniff";
            }
            await next(ctx);
            return;
        }

        if (!ctx.Response.HasStarted)
        {
            var nonceBytes = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(nonceBytes);
            var nonce = Convert.ToBase64String(nonceBytes);
            ctx.Items["csp-nonce"] = nonce;

            var scriptSrc = cspOptions.ScriptSrc.Contains("'nonce-'")
                ? cspOptions.ScriptSrc.Replace("'nonce-'", $"'nonce-{nonce}'")
                : $"{cspOptions.ScriptSrc} 'nonce-{nonce}'";

            var styleSrc = cspOptions.StyleSrc.Contains("'nonce-'")
                ? cspOptions.StyleSrc.Replace("'nonce-'", $"'nonce-{nonce}'")
                : $"{cspOptions.StyleSrc} 'nonce-{nonce}'";

            ctx.Response.Headers.ContentSecurityPolicy =
                $"default-src {cspOptions.DefaultSrc}; " +
                $"script-src {scriptSrc}; " +
                $"worker-src {cspOptions.WorkerSrc}; " +
                $"style-src {styleSrc}; " +
                $"img-src {cspOptions.ImgSrc}; " +
                $"font-src {cspOptions.FontSrc}; " +
                $"connect-src {cspOptions.ConnectSrc}; " +
                $"frame-ancestors {cspOptions.FrameAncestors}; " +
                $"base-uri {cspOptions.BaseUri}; " +
                $"form-action {cspOptions.FormAction}";

            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.Headers.XFrameOptions = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
        }
        await next(ctx);
    }

    private static void UseCsrfValidation(WebApplication app)
    {
        // Single source of truth with the CORS policy: both read the bound
        // CorsOptions.AllowedOrigins. Browsers attach an Origin header to
        // every cross-site request, so rejecting state-changing /api requests
        // whose Origin is neither explicitly allowed nor this deployment's own
        // origin blocks login CSRF and every other unauthenticated cross-site
        // write (antiforgery tokens only cover authenticated requests).
        // Non-browser clients send no Origin at all.
        var allowedOrigins = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value.AllowedOrigins;

        app.Use(async (ctx, next) =>
        {
            var method = ctx.Request.Method.ToUpperInvariant();
            var isAuthenticated = ctx.User.Identity?.IsAuthenticated == true;
            var isApiPath = ctx.Request.Path.StartsWithSegments("/api");

            if (!app.Environment.IsDevelopment() && isApiPath && IsUnsafeMethod(method))
            {
                var requestOrigin = $"{ctx.Request.Scheme}://{ctx.Request.Host.Value}";
                if (IsForeignOrigin(ctx.Request.Headers.Origin.ToString(), requestOrigin, allowedOrigins))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Detail = "Cross-origin request rejected.",
                        Status = 403,
                        Title = "Forbidden",
                        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    };
                    await ctx.Response.WriteAsJsonAsync(problem, JsonOptions);
                    return;
                }
            }

            if (ShouldValidateCsrfRequest(
                    method,
                    isAuthenticated,
                    isApiPath,
                    app.Environment.IsDevelopment(),
                    ctx.Request.Path,
                    out var rejectedBecauseAnonymousApi))
            {
                var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
                try { await antiforgery.ValidateRequestAsync(ctx); }
                catch (AntiforgeryValidationException)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Detail = "CSRF validation failed.",
                        Status = 403,
                        Title = "Forbidden",
                        Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    };
                    await ctx.Response.WriteAsJsonAsync(problem, JsonOptions);
                    return;
                }
            }
            else if (rejectedBecauseAnonymousApi)
            {
                // An unauthenticated state-changing request hit an API route
                // that is not on the anonymous-mutation allowlist. There is no
                // antiforgery token to validate (the request carries no auth),
                // so reject it outright rather than silently letting it through
                // and relying on the controller to refuse.
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Detail = "Unauthenticated state-changing request rejected.",
                    Status = 403,
                    Title = "Forbidden",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                };
                await ctx.Response.WriteAsJsonAsync(problem, JsonOptions);
                return;
            }
            await next();
        });
    }

    private static bool IsUnsafeMethod(string method) => method is not ("GET" or "HEAD" or "OPTIONS" or "TRACE");

    /// <summary>
    /// Decides whether a request's <c>Origin</c> must be rejected. Absent
    /// Origin means a non-browser client (curl, native apps) — always allowed.
    /// Present Origin must match either an explicitly allowed origin or the
    /// origin this request was addressed to (same-origin SPA traffic).
    /// </summary>
    internal static bool IsForeignOrigin(string? origin, string requestOrigin, IReadOnlyList<string> allowedOrigins)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        return !MatchesOrigin(origin, requestOrigin) && !allowedOrigins.Any(o => MatchesOrigin(origin, o));
    }

    private static bool MatchesOrigin(string origin, string expected)
    {
        // Normalize through Uri so an explicitly-defaulted port ("https://host:443")
        // compares equal to the implicit form ("https://host"), and scheme/host are
        // case-insensitively lowercased. Falls back to a trim/compare on values that
        // fail to parse as absolute URIs.
        if (TryNormalizeOrigin(origin, out var a) && TryNormalizeOrigin(expected, out var b))
        {
            return a == b;
        }

        static string Normalize(string value) => value.Trim().TrimEnd('/').ToLowerInvariant();
        return Normalize(origin) == Normalize(expected);
    }

    private static bool TryNormalizeOrigin(string value, out string normalized)
    {
        normalized = string.Empty;
        if (Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Scheme))
        {
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            normalized = $"{uri.Scheme.ToLowerInvariant()}://{uri.Host}{port}".ToLowerInvariant();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Anonymous state-changing API endpoints. An unauthenticated request that
    /// reaches the CSRF middleware has already passed <c>UseAuthorization</c>,
    /// so it can only target an <c>[AllowAnonymous]</c> controller action. Those
    /// actions are enumerated here so every other anonymous mutation to /api is
    /// rejected instead of silently skipping antiforgery validation.
    /// </summary>
    internal static readonly string[] AnonymousMutatingApiPaths =
    [
        "/api/signin",
        "/api/refresh",
        // Authorized admin sign-up carries its own CSRF control (the
        // X-Admin-Signup custom header, which cross-site forms cannot set).
        "/api/admin/authorized-signup",
        // Deprecated self-registration — allowlisted so it still returns the
        // intentional 410 Gone instead of a misleading 403.
        "/api/signup",
    ];

    internal static bool IsAnonymousMutatingApiPath(string path)
        => AnonymousMutatingApiPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Decides whether the CSRF middleware must validate the request's
    /// <c>X-CSRF-Token</c> header. Non-GET requests are only validated when they
    /// are authenticated. API requests are enforced outside Development: in Vite
    /// dev the SPA page carries no <c>csrf-token</c> meta and the Secure
    /// antiforgery cookie is never sent over plain HTTP, so validating there
    /// would break every state-changing request. <c>/api/logs</c> is exempt
    /// because the SPA flushes logs at unload via <c>sendBeacon()</c>, which
    /// cannot set headers — its CSRF control is the SameSite=Lax session cookie.
    /// Anonymous state-changing API mutations are never antiforgery-validated
    /// (there is no token), but any such request whose path is not on
    /// <see cref="AnonymousMutatingApiPaths"/> is flagged via
    /// <paramref name="rejectedBecauseAnonymousApi"/> so the middleware rejects it.
    /// </summary>
    internal static bool ShouldValidateCsrfRequest(
        string method,
        bool isAuthenticated,
        bool isApiPath,
        bool isDevelopment,
        string path,
        out bool rejectedBecauseAnonymousApi)
    {
        rejectedBecauseAnonymousApi = false;
        if (method is "GET" or "HEAD" or "OPTIONS" or "TRACE")
        {
            return false;
        }
        if (!isAuthenticated)
        {
            // Auth/authorization already run before this middleware: an
            // otherwise-protected endpoint would have returned 401/403 before
            // reaching here, so an anonymous unsafe /api request can only target
            // an [AllowAnonymous] action. Reject any that is not allowlisted.
            if (isApiPath && !IsAnonymousMutatingApiPath(path))
            {
                rejectedBecauseAnonymousApi = true;
            }
            return false;
        }
        if (path.Equals("/api/logs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !isApiPath || !isDevelopment;
    }

    private static void UseApiEndpoints(WebApplication app)
    {
        // OpenAPI/Scalar are development tooling. Never expose the API surface
        // in production — discovery aids an attacker's reconnaissance.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options.WithTheme(ScalarTheme.Purple);
            });
        }

        app.MapControllers();

        // Liveness answers "is the process responsive" and must NOT probe
        // dependencies — otherwise a database blip would restart healthy pods.
        // Readiness answers "can this pod serve traffic" and includes the DB.
        //
        // /health and /api/health remain DB-aware, unauthenticated BY DESIGN:
        // the k8s startup probe and the external monitoring ingress
        // (nars-infra/k8s/ingress-api.yaml) require anonymous plain-HTTP
        // access. The response carries only the aggregate status string — no
        // per-check details — so exposure is limited to up/down state.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/api/health");
    }

    private static void LogStartupComplete(WebApplication app, IConfiguration config, ILogger<Program> logger)
        => app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Urls.Count != 0
                ? string.Join(", ", app.Urls)
                : config["ASPNETCORE_URLS"] ?? "http://localhost:5000";
            logger.LogInformation("Startup complete — {Addresses}", addresses);
        });
}
