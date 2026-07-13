using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
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

        await dbCtx.Database.CanConnectAsync();
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
                var name = ctx.File.Name;
                if (name is "index.html" or "login.html")
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "no-store, no-cache, must-revalidate");
                    ctx.Context.Response.Headers.Append("Pragma", "no-cache");
                    return;
                }
                if (name.EndsWith(".js") || name.EndsWith(".mjs") || name.EndsWith(".css") || name.EndsWith(".woff2"))
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
                }
            }
        });
    }

    private static void UseSecurityMiddleware(WebApplication app)
    {
        app.UseHsts();
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseRouting();
        app.UseCors();
        app.UseResponseCompression();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        var cspOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CspOptions>>().Value;

        app.Use(async (ctx, next) =>
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
            await next();
        });
    }

    private static void UseCsrfValidation(WebApplication app)
        => app.Use(async (ctx, next) =>
        {
            var method = ctx.Request.Method.ToUpperInvariant();
            var isAuthenticated = ctx.User.Identity?.IsAuthenticated == true;
            var isApiPath = ctx.Request.Path.StartsWithSegments("/api");
            if (method is not ("GET" or "HEAD" or "OPTIONS" or "TRACE")
                && isAuthenticated
                && !isApiPath)
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

    private static void UseApiEndpoints(WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.WithTheme(ScalarTheme.Purple);
        });

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
