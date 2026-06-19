using System.Security.Claims;

namespace NarsApi.Services;

public interface IJwtService
{
    string CreateToken(Guid userId, string username, string name, string email, int? communeId,
        string role = "commune_user", int? dairaId = null, int? wilayaId = null);

    ClaimsPrincipal? ValidateToken(string token);

    (string rawToken, string hash) CreateRefreshToken();

    TimeSpan AccessTokenExpiresIn { get; }
}
