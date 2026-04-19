using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace NarsApi.Services;

/// <summary>
/// Service for creating and validating JWT tokens.
/// The configuration (secret, issuer, audience) is injected via the constructor
/// to ensure consistency with the Authentication middleware.
/// </summary>
public class JwtService(string secret, string? issuer, string? audience, IConfiguration config, ILogger<JwtService> logger)
{
    private readonly string _secret = secret ?? throw new ArgumentNullException(nameof(secret));
    private readonly int _expiresMinutes = ParseIntConfig(config["Jwt:ExpiresInMinutes"], 1440);
    private readonly int _refreshExpiresDays = ParseIntConfig(config["Jwt:RefreshExpiresInDays"], 30);
    private readonly string? _issuer = issuer;
    private readonly string? _audience = audience;

    /// <summary>
    /// Safely parse an integer configuration value, falling back to a default.
    /// </summary>
    private static int ParseIntConfig(string? value, int defaultValue)
    {
        if (int.TryParse(value, out var result)) return result;
        return defaultValue;
    }

    public string CreateToken(Guid userId, string username, string name, string email, int communeId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("user_id",    userId.ToString()),
            new Claim("username",   username),
            new Claim("name",       name),
            new Claim("email",      email),
            new Claim("commune_id", communeId.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiresMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates a refresh token — a cryptographically random string.
    /// The server stores the hash; the client stores the raw token in an HttpOnly cookie.
    /// </summary>
    public static (string rawToken, string hash) CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return (raw, hash);
    }

    /// <summary>
    /// Validates a raw JWT string and returns the principal on success, or null
    /// if the token is missing, expired, or tampered with.
    /// Used by <see cref="NarsApi.Controllers.PagesController"/> to guard
    /// server-rendered HTML page routes before the SPA boots.
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = !string.IsNullOrEmpty(_issuer),
                    ValidateAudience = !string.IsNullOrEmpty(_audience),
                    ValidIssuer = _issuer,
                    ValidAudience = _audience,
                    ClockSkew = TimeSpan.Zero,
                }, out _);
        }
        catch (SecurityTokenException ex)
        {
            logger.LogDebug(ex, "JWT validation failed: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during JWT validation");
            return null;
        }
    }
}
