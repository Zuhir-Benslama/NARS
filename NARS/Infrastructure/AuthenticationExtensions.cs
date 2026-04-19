using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuer = !string.IsNullOrEmpty(issuer),
                    ValidateAudience = !string.IsNullOrEmpty(audience),
                    ClockSkew = TimeSpan.Zero,
                };

                if (!string.IsNullOrEmpty(issuer))
                    validationParams.ValidIssuer = issuer;

                if (!string.IsNullOrEmpty(audience))
                    validationParams.ValidAudience = audience;

                options.TokenValidationParameters = validationParams;

                // Read token from HttpOnly cookie
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Cookies["access_token"];
                        if (!string.IsNullOrEmpty(token))
                        {
                            ctx.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        var loggerFactory = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("NarsApi.Auth");
                        logger.LogWarning("[Auth] Authentication failed for {Path}: {Message}",
                            ctx.Request.Path, ctx.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        var loggerFactory = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("NarsApi.Auth");
                        logger.LogInformation("[Auth] Challenging {Path} (401)", ctx.Request.Path);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        // Register JwtService with the same secret and options used for authentication
        services.AddScoped<JwtService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<JwtService>>();
            return new JwtService(jwtSecret, issuer, audience, config, logger);
        });

        return services;
    }
}
