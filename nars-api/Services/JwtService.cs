using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

/// <summary>
/// Service for creating and validating JWT tokens.
/// The configuration (secret, issuer, audience) is injected via the constructor
/// to ensure consistency with the Authentication middleware.
/// </summary>
public sealed class JwtService(string secret, string? issuer, string? audience, IOptions<JwtOptions> jwtOptions, ILogger<JwtService> logger, IDateTimeProvider timeProvider) : IJwtService
{
    // Thread-safe: JwtSecurityTokenHandler is safe for concurrent reads after initialization.
    // MapInboundClaims=false keeps claim types verbatim ("role", "email", ...) instead of
    // remapping to the long URI claim types — matching the JwtBearer pipeline
    // (AuthenticationExtensions) so both validation paths produce identical principals.
    private static readonly JwtSecurityTokenHandler TokenHandler = new()
    {
        MapInboundClaims = false,
    };
    private readonly int _expiresMinutes = jwtOptions.Value.ExpiresInMinutes;
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(secret));
    public TimeSpan AccessTokenExpiresIn => TimeSpan.FromMinutes(_expiresMinutes);

    public string CreateToken(Guid userId, string username, string name, string email, int? communeId,
        string role = "commune_user", int? dairaId = null, int? wilayaId = null)
    {
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimNames.UserId,  userId.ToString()),
            new(ClaimNames.Username, username),
            new(ClaimNames.Name,    name),
            new(ClaimNames.Email,   email),
            new(ClaimNames.Role,    role),
        };

        if (communeId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.CommuneId, communeId.Value.ToString()));
        }

        if (dairaId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.DairaId, dairaId.Value.ToString()));
        }

        if (wilayaId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.WilayaId, wilayaId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: timeProvider.UtcNow.AddMinutes(_expiresMinutes),
            signingCredentials: creds
        );

        return TokenHandler.WriteToken(token);
    }

    public (string rawToken, string hash) CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return (raw, hash);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return TokenHandler.ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _key,
                    ValidateIssuer = !string.IsNullOrEmpty(issuer),
                    ValidateAudience = !string.IsNullOrEmpty(audience),
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var now = timeProvider.UtcNow;
                        return (notBefore == null || notBefore.Value <= now)
                            && expires != null && expires.Value > now;
                    },
                    ClockSkew = TimeSpan.Zero,
                }, out _);
        }
        catch (SecurityTokenException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "JWT validation failed: {Message}", ex.Message);
            }
            return null;
        }
    }
}
