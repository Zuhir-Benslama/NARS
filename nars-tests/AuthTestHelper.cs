using System.Security.Claims;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Tests;

public static class AuthTestHelper
{
    /// <summary>Shared test JWT secret key (min 32 chars for HMAC-SHA256).</summary>
    public const string TestJwtSecret = "test-secret-key-that-is-at-least-32-chars-long!!";

    public static IUserCreationService CreateUserCreationMock(AppDbContext? db = null)
    {
        var mock = new Mock<IUserCreationService>();
        mock.Setup(s => s.ValidateAndCreateUserAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string email, string phone, string username,
                string password, string role, int? communeId, int? dairaId, int? wilayaId,
                CancellationToken _) =>
            {
                if (db is not null)
                {
                    if (db.Users.Any(u => u.Username == username.ToLowerInvariant()))
                        return ((User?)null, "Username already exists.");
                    if (db.Users.Any(u => u.Email == email))
                        return ((User?)null, "Email already exists.");
                }

                var pwdError = Infrastructure.PasswordValidator.Validate(password);
                if (pwdError is not null)
                    return ((User?)null, pwdError);

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    Username = username.ToLowerInvariant(),
                    Email = email,
                    Name = name,
                    Phone = phone,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                    Role = role,
                    CommuneId = communeId,
                    DairaId = dairaId,
                    WilayaId = wilayaId,
                };
                return (user, (string?)null);
            });
        return mock.Object;
    }

    public static ClaimsPrincipal CreateClaimsPrincipal(
        Guid userId, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null,
        string username = "testuser")
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, userId.ToString()),
            new(ClaimNames.Username, username),
            new(ClaimNames.Role, role),
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

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
