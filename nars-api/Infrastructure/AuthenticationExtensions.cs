using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
    /// </summary>
    /// <param name="algorithm">
    /// The configured signing algorithm (Jwt:Algorithm). Validation accepts ONLY
    /// this algorithm — the same one JwtService signs with — so both validation
    /// paths stay consistent and cross-algorithm tokens are rejected everywhere.
    /// </param>
    public static IServiceCollection AddNarsJwtAuthentication(
        this IServiceCollection services,
        string jwtSecret,
        string? issuer = null,
        string? audience = null,
        string algorithm = "HS256")
    {
        var signingAlgorithm = MapSigningAlgorithm(algorithm);

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
                        var current = await stampCache.GetStampAsync(userId.Value, ctx.HttpContext.RequestAborted);

                        if (current is null)
                        {
                            // Cache miss — query DB and populate cache for next request.
                            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                            current = await db.Users.AsNoTracking()
                                .Where(u => u.Id == userId.Value)
                                .Select(u => u.SecurityStamp)
                                .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

                            if (current is not null)
                            {
                                stampCache.SetStamp(userId.Value, current);
                            }
                        }

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

        services.AddAuthorization(options =>
        {
            // Backs [Authorize(Policy = "CanReviewFeatures")] on the
            // AI draft-feature accept/reject endpoints.
            options.AddPolicy("CanReviewFeatures", policy => policy.RequireAssertion(ctx =>
            {
                var role = ctx.User.FindFirstValue(ClaimNames.Role);
                return UserRoles.IsDraftReviewer(role);
            }));
        });

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
