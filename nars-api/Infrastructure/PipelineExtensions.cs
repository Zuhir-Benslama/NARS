using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
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
        UseStaticFilesWithCaching(app);
        UseSecurityMiddleware(app);
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

        var canConnect = await dbCtx.Database.CanConnectAsync();
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

                    if (ex is ArgumentException or InvalidOperationException)
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

        app.Use(async (HttpContext ctx, RequestDelegate next) =>
            await ApplyCspMiddlewareAsync(ctx, next, cspOptions));
    }

    /// <summary>
    /// Sets the per-request CSP nonce + security headers on non-API pages
    /// (<c>/login</c>, <c>/map</c>). The nonce is stashed in
    /// <c>ctx.Items["csp-nonce"]</c> so <see cref="NarsApi.Controllers.PagesController"/>
    /// can inject it into inline script tags, and embedded into script-src/style-src
    /// so <c>'unsafe-inline'</c> is never sent in production.
    /// </summary>
    internal static async Task ApplyCspMiddlewareAsync(
        HttpContext ctx,
        RequestDelegate next,
        CspOptions cspOptions)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api") && !ctx.Response.HasStarted)
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
        => app.Use(async (ctx, next) =>
        {
            var method = ctx.Request.Method.ToUpperInvariant();
            var isAuthenticated = ctx.User.Identity?.IsAuthenticated == true;
            var isApiPath = ctx.Request.Path.StartsWithSegments("/api");
            if (ShouldValidateCsrf(
                    method,
                    isAuthenticated,
                    isApiPath,
                    app.Environment.IsDevelopment(),
                    ctx.Request.Path))
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
            await next();
        });

    /// <summary>
    /// Decides whether the CSRF middleware must validate the request's
    /// <c>X-CSRF-Token</c> header. Non-GET requests are only validated when they
    /// are authenticated. API requests are enforced outside Development: in Vite
    /// dev the SPA page carries no <c>csrf-token</c> meta and the Secure
    /// antiforgery cookie is never sent over plain HTTP, so validating there
    /// would break every state-changing request. <c>/api/logs</c> is exempt
    /// because the SPA flushes logs at unload via <c>sendBeacon()</c>, which
    /// cannot set headers — its CSRF control is the SameSite=Lax session cookie.
    /// </summary>
    internal static bool ShouldValidateCsrf(
        string method,
        bool isAuthenticated,
        bool isApiPath,
        bool isDevelopment,
        string path)
    {
        if (method is "GET" or "HEAD" or "OPTIONS" or "TRACE")
        {
            return false;
        }
        if (!isAuthenticated)
        {
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
