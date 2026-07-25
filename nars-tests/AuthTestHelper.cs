using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>
    /// Creates a test double for <see cref="IUserCreationService"/>.
    /// Validates password strength and DB uniqueness (same checks as the real service)
    /// but skips BCrypt hashing — uses a placeholder hash instead.
    /// </summary>
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
#pragma warning disable CA1862 // EF Core cannot translate string.Equals with StringComparison to SQL
                    if (db.Users.Any(u => u.Username == username.ToLowerInvariant()))
#pragma warning restore CA1862
                        return ((User?)null, "Username already exists.");
                    if (db.Users.Any(u => u.Email == email))
                        return ((User?)null, "Email already exists.");
                }

                var pwdError = PasswordValidator.Validate(password);
                if (pwdError is not null)
                    return ((User?)null, pwdError);

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    Username = username.ToLowerInvariant(),
                    Email = email,
                    Name = name,
                    Phone = phone,
                    PasswordHash = "test-hash",
                    Role = role,
                    CommuneId = communeId,
                    DairaId = dairaId,
                    WilayaId = wilayaId,
                };
                return (user, (string?)null);
            });
        mock.Setup(s => s.SaveUserAsync(
                It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Returns((User user, CancellationToken _) =>
            {
                if (db is not null)
                {
                    db.Users.Add(user);
                    return db.SaveChangesAsync();
                }
                return Task.CompletedTask;
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

    public static void SetUser<T>(T controller, Guid userId, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null, string? username = null)
        where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreateClaimsPrincipal(userId, role, communeId, dairaId, wilayaId, username ?? "testuser")
            }
        };
    }
}
