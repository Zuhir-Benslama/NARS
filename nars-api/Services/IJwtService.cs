using System.Security.Claims;

namespace NarsApi.Services;

public interface IJwtService
{
    string CreateToken(Guid userId, string username, string name, string email, int? communeId,
        string role = "commune_user", int? dairaId = null, int? wilayaId = null);

    /// <summary>Validates a JWT access token and returns the claims principal, or null if invalid/expired.</summary>
    ClaimsPrincipal? ValidateToken(string token);

    (string rawToken, string hash) CreateRefreshToken();

    TimeSpan AccessTokenExpiresIn { get; }
}
