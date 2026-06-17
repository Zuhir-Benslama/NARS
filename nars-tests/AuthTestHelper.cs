using System.Security.Claims;

namespace NarsApi.Tests;

public static class AuthTestHelper
{
    public static ClaimsPrincipal CreateClaimsPrincipal(
        Guid userId, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null,
        string username = "testuser")
    {
        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new("username", username),
            new("role", role),
        };

        if (communeId.HasValue) claims.Add(new Claim("commune_id", communeId.Value.ToString()));
        if (dairaId.HasValue) claims.Add(new Claim("daira_id", dairaId.Value.ToString()));
        if (wilayaId.HasValue) claims.Add(new Claim("wilaya_id", wilayaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
