using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NarsApi.Services;

namespace NarsApi.Infrastructure;

/// <summary>
/// Extension methods for JWT authentication configuration.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication reading tokens from HttpOnly cookies.
    /// </summary>
    public static IServiceCollection AddNarsJwtAuthentication(
        this IServiceCollection services,
        string jwtSecret,
        string? issuer = null,
        string? audience = null)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Per-instance (not global) opt-out of claim renaming.
                // MapInboundClaims=false keeps "role" as "role" instead of
                // remapping to the long URI claim type, which would break
                // User.FindFirstValue("role") in NarsControllerBase.CurrentUserRole.
                options.MapInboundClaims = false;

                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuer = !string.IsNullOrEmpty(issuer),
                    ValidateAudience = !string.IsNullOrEmpty(audience),
                    ClockSkew = TimeSpan.Zero,
                };

                if (!string.IsNullOrEmpty(issuer))
                {
                    validationParams.ValidIssuer = issuer;
                }

                if (!string.IsNullOrEmpty(audience))
                {
                    validationParams.ValidAudience = audience;
                }

                options.TokenValidationParameters = validationParams;

                // Read token from HttpOnly cookie
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Cookies[CookieNames.AccessToken];
                        if (!string.IsNullOrEmpty(token))
                        {
                            ctx.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NarsApi.Auth");
                        logger.LogWarning("[Auth] Authentication failed for {Path}: {Message}",
                            ctx.Request.Path, ctx.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NarsApi.Auth");
                        logger.LogInformation("[Auth] Challenging {Path} (401)", ctx.Request.Path);
                        return Task.CompletedTask;
                    }
                };
            })
            .AddCookie("Pages", options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                options.SlidingExpiration = true;
                options.LoginPath = "/login";
            });

        services.AddAuthorization();

        // Register JwtService with the same secret and options used for authentication
        services.AddScoped<IJwtService, JwtService>(sp =>
        {
            var jwtOptions = sp.GetRequiredService<IOptions<JwtOptions>>();
            var logger = sp.GetRequiredService<ILogger<JwtService>>();
            var timeProvider = sp.GetRequiredService<IDateTimeProvider>();
            return new JwtService(jwtSecret, issuer, audience, jwtOptions, logger, timeProvider);
        });

        return services;
    }
}
