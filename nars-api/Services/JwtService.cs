using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using static NarsApi.Infrastructure.SqlFragments;

namespace NarsApi.Services;

/// <summary>
/// Service for creating and validating JWT tokens.
/// The configuration (secret, issuer, audience) is injected via the constructor
/// to ensure consistency with the Authentication middleware.
/// </summary>
public class JwtService(string secret, string? issuer, string? audience, IConfiguration config, ILogger<JwtService> logger, IDateTimeProvider timeProvider) : IJwtService
{
    private readonly string _secret = secret ?? throw new ArgumentNullException(nameof(secret));
    private readonly int _expiresMinutes = ParseIntConfig(config["Jwt:ExpiresInMinutes"], 1440);
    public TimeSpan AccessTokenExpiresIn => TimeSpan.FromMinutes(_expiresMinutes);
    private readonly string? _issuer = issuer;
    private readonly string? _audience = audience;

    public string CreateToken(Guid userId, string username, string name, string email, int? communeId,
        string role = "commune_user", int? dairaId = null, int? wilayaId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("user_id",  userId.ToString()),
            new("username", username),
            new("name",     name),
            new("email",    email),
            new("role",     role),
        };

        // Only include geographic claims that are relevant to this role.
        // This keeps tokens minimal and avoids confusion when a claim is present
        // but meaningless for the role.
        if (communeId.HasValue) claims.Add(new Claim("commune_id", communeId.Value.ToString()));
        if (dairaId.HasValue) claims.Add(new Claim("daira_id", dairaId.Value.ToString()));
        if (wilayaId.HasValue) claims.Add(new Claim("wilaya_id", wilayaId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: timeProvider.UtcNow.AddMinutes(_expiresMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates a refresh token — a cryptographically random string.
    /// The server stores the hash; the client stores the raw token in an HttpOnly cookie.
    /// </summary>
    public (string rawToken, string hash) CreateRefreshToken()
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
