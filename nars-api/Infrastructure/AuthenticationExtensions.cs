using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NarsApi.Data;
using NarsApi.Services;

namespace NarsApi.Infrastructure;

/// <summary>
/// Extension methods for JWT authentication configuration.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication reading tokens from HttpOnly cookies.
    /// The signing algorithm is taken from the already-bound, DataAnnotations-
    /// validated <see cref="JwtOptions"/> — the same source the
    /// <c>JwtService</c> factory reads — so there is a single source of truth.
    /// </summary>
    public static IServiceCollection AddNarsJwtAuthentication(
        this IServiceCollection services,
        string jwtSecret,
        string? issuer = null,
        string? audience = null)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Dependency-injected options configuration: resolves the validated
        // JwtOptions when the JwtBearerOptions are first built (runtime, after
        // the container is complete), so the algorithm never has to be re-read
        // from raw config at registration time.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var signingAlgorithm = MapSigningAlgorithm(jwtOptions.Value.Algorithm);

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
                    // Restrict validation to exactly the configured HS algorithm
                    // the tokens are signed with, closing off algorithm swaps.
                    ValidAlgorithms = [signingAlgorithm],
                    // Claims are kept verbatim (MapInboundClaims=false above), so tell the
                    // principal which raw claim types map to role and name. Without this,
                    // RoleClaimType defaults to the ClaimTypes.Role URI and every
                    // [Authorize(Roles = ...)] check fails with 403.
                    RoleClaimType = ClaimNames.Role,
                    NameClaimType = ClaimNames.Username,
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
                    OnTokenValidated = async ctx =>
                    {
                        // The security stamp is a random per-user value embedded
                        // in the JWT and rotated on lockout/password change.
                        // Re-checking it here means a rotated stamp immediately
                        // invalidates every previously issued access token
                        // (stateless JWTs would otherwise stay valid to expiry).
                        var userId = Guid.TryParse(ctx.Principal?.FindFirstValue(ClaimNames.UserId),
                            out var id) ? id : (Guid?)null;
                        var stamp = ctx.Principal?.FindFirstValue(ClaimNames.SecurityStamp);

                        if (userId is null || string.IsNullOrEmpty(stamp))
                        {
                            ctx.Fail("Token is missing identity claims.");
                            return;
                        }

                        var stampCache = ctx.HttpContext.RequestServices.GetRequiredService<ISecurityStampCache>();
                        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var current = await stampCache.GetStampWithDbFallbackAsync(db, userId.Value, ctx.HttpContext.RequestAborted);

                        if (current != stamp)
                        {
                            ctx.Fail("Session has been invalidated (security stamp rotated).");
                        }
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NarsApi.Auth");
                        logger.LogWarning("[Auth] Authentication failed for {Path}: {Message}",
                            (ctx.Request.Path.Value ?? string.Empty).ReplaceLineEndings(" "),
                            ctx.Exception.Message.ReplaceLineEndings(" "));
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("NarsApi.Auth");
                        logger.LogInformation("[Auth] Challenging {Path} (401)",
                            (ctx.Request.Path.Value ?? string.Empty).ReplaceLineEndings(" "));
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("CanReviewFeatures", policy => policy.RequireAssertion(ctx =>
            {
                var role = ctx.User.FindFirstValue(ClaimNames.Role);
                return UserRoles.IsDraftReviewer(role);
            }));

        // Register JwtService so its factory resolves JwtOptions through the
        // container, sharing the same validated instance used for authentication.
        services.AddScoped<IJwtService, JwtService>(sp =>
        {
            var jwtOptions = sp.GetRequiredService<IOptions<JwtOptions>>();
            var logger = sp.GetRequiredService<ILogger<JwtService>>();
            var timeProvider = sp.GetRequiredService<IDateTimeProvider>();
            return new JwtService(jwtSecret, issuer, audience, jwtOptions, logger, timeProvider);
        });

        return services;
    }

    /// <summary>
    /// Maps the configuration key (HS256/HS384/HS512 — the same values
    /// <see cref="JwtOptions.Algorithm"/> allowlists via DataAnnotations) to the
    /// corresponding SecurityAlgorithms identifier. Unknown values fail fast.
    /// </summary>
    private static string MapSigningAlgorithm(string algorithm) => algorithm switch
    {
        "HS256" => SecurityAlgorithms.HmacSha256,
        "HS384" => SecurityAlgorithms.HmacSha384,
        "HS512" => SecurityAlgorithms.HmacSha512,
        _ => throw new InvalidOperationException(
            $"Unsupported Jwt:Algorithm '{algorithm}'. Expected HS256, HS384 or HS512."),
    };
}
