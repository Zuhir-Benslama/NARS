using System.Security.Claims;

namespace NarsApi.Services;

public interface IJwtService
{
    /// <summary>Creates a signed JWT access token with user claims.</summary>
    string CreateToken(Guid userId, string username, string name, string email, int? communeId,
        string role = "commune_user", int? dairaId = null, int? wilayaId = null);

    /// <summary>Validates a JWT access token and returns the claims principal, or null if invalid/expired.</summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>Generates a cryptographically random refresh token and its SHA-256 hash.</summary>
    (string rawToken, string hash) CreateRefreshToken();

    /// <summary>Gets the access token lifetime duration.</summary>
    TimeSpan AccessTokenExpiresIn { get; }
}
