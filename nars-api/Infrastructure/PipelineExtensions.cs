using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using Scalar.AspNetCore;

namespace NarsApi.Infrastructure;

public static class PipelineExtensions
{
    public static async Task<WebApplication> ConfigureNarsPipelineAsync(this WebApplication app, IConfiguration config, bool logJwtWarning)
    {
        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

        if (logJwtWarning)
        {
            startupLogger.LogWarning("JWT Issuer/Audience validation is disabled. Set Jwt:Issuer and Jwt:Audience for defense-in-depth.");
        }

        using (var scope = app.Services.CreateScope())
        {
            var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            startupLogger.LogInformation("==================================================");
            startupLogger.LogInformation("NARS - ASP.NET Core + PostgreSQL/PostGIS");
            startupLogger.LogInformation("==================================================");

            await dbCtx.Database.CanConnectAsync();
            startupLogger.LogInformation("Database connection verified");

            await dbCtx.Database.MigrateAsync();
            startupLogger.LogInformation("Database migrations applied");
        }

        app.UseExceptionHandler(errApp =>
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
                    logger.LogError(ex, "Unhandled exception");
                }

                var message = app.Environment.IsDevelopment()
                    ? ex?.Message ?? "An unexpected error occurred."
                    : "An internal server error occurred. Please try again.";

                var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Detail = message,
                    Status = 500,
                    Title = "Internal Server Error",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                };
                await ctx.Response.WriteAsync(
                    JsonSerializer.Serialize(problem, new JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            });
        });

        app.UseDefaultFiles();

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".js"] = "text/javascript";
        contentTypeProvider.Mappings[".mjs"] = "text/javascript";
        contentTypeProvider.Mappings[".css"] = "text/css";
        contentTypeProvider.Mappings[".woff2"] = "font/woff2";
        contentTypeProvider.Mappings[".woff"] = "font/woff";
        contentTypeProvider.Mappings[".ico"] = "image/x-icon";
        contentTypeProvider.Mappings[".svg"] = "image/svg+xml";
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

        app.UseRouting();

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseCors();
        app.UseResponseCompression();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments("/api") && !ctx.Response.HasStarted)
            {
                var nonceBytes = new byte[16];
                System.Security.Cryptography.RandomNumberGenerator.Fill(nonceBytes);
                var nonce = Convert.ToBase64String(nonceBytes);
                ctx.Items["csp-nonce"] = nonce;

                ctx.Response.Headers.ContentSecurityPolicy =
                    "default-src 'self'; " +
                    $"script-src 'self' 'nonce-{nonce}' blob:; " +
                    "worker-src 'self' blob:; " +
                    "style-src 'self' https://cdn.jsdelivr.net https://unpkg.com 'unsafe-inline' https://fonts.googleapis.com; " +
                    "img-src 'self' data: blob: https://*.tile.openstreetmap.org https://*.basemaps.cartocdn.com https://*.arcgisonline.com; " +
                    "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
                    "connect-src 'self' https: data: ws://127.0.0.1:* http://127.0.0.1:* https://*.arcgisonline.com https://*.tile.openstreetmap.org https://*.basemaps.cartocdn.com; " +
                    "frame-ancestors 'none'; " +
                    "base-uri 'self'; " +
                    "form-action 'self'";

                ctx.Response.Headers.XContentTypeOptions = "nosniff";
                ctx.Response.Headers.XFrameOptions = "DENY";
                ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                ctx.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
            }
            await next();
        });

        app.Use(async (ctx, next) =>
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
                    await ctx.Response.WriteAsJsonAsync(problem, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                    return;
                }
            }
            await next();
        });

        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.WithTheme(ScalarTheme.Purple);
        });

        app.MapControllers();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/api/health");

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Urls.Count != 0
                ? string.Join(", ", app.Urls)
                : config["ASPNETCORE_URLS"] ?? "http://localhost:5000";
            if (startupLogger.IsEnabled(LogLevel.Information))
            {
                startupLogger.LogInformation("Startup complete — {Addresses}", addresses);
            }
        });

        return app;
    }
}
