using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace NarsApi.Services;

public class JwtService(IConfiguration config)
{
    private readonly string _secret = config["Jwt:SecretKey"]
        ?? throw new InvalidOperationException("Jwt:SecretKey not configured");
    private readonly int _expiresMinutes = int.Parse(config["Jwt:ExpiresInMinutes"] ?? "1440");

    public string CreateToken(int userId, string username, string name, string email, int communeId)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
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
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(_expiresMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
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
                    IssuerSigningKey         = key,
                    ValidateIssuer           = false,
                    ValidateAudience         = false,
                    ClockSkew                = TimeSpan.Zero,
                }, out _);
        }
        catch
        {
            return null;
        }
    }
}
