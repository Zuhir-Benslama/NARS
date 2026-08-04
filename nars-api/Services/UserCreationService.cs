using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class UserCreationService(AppDbContext db) : IUserCreationService
{
    public async Task<UserCreationResult> ValidateAndCreateUserAsync(
        string name,
        string email,
        string phone,
        string username,
        string password,
        string role,
        int? communeId,
        int? dairaId,
        int? wilayaId,
        CancellationToken cancellationToken = default)
    {
        // 1. Geographic fields present.
        var geoError = GeographicValidator.Validate(role, communeId, dairaId, wilayaId);
        if (geoError is not null)
        {
            return UserCreationResult.Failure(UserCreationErrorCode.Invalid, geoError);
        }

        // 2. Uniqueness (normalised to lowercase for case-insensitive matching).
        var normalizedUsername = username.ToLowerInvariant();
        var normalizedEmail = email.ToLowerInvariant();
        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Username == normalizedUsername || u.Email == normalizedEmail, cancellationToken);
        if (existing is not null)
        {
            var field = existing.Username == normalizedUsername ? "Username" : "Email";
            return UserCreationResult.Failure(UserCreationErrorCode.Duplicate, $"{field} already exists.");
        }

        // 3. Password strength.
        var pwdErr = PasswordValidator.Validate(password);
        if (pwdErr is not null)
        {
            return UserCreationResult.Failure(UserCreationErrorCode.Invalid, pwdErr);
        }

        // 4. Create the user entity.
        var newUser = new User
        {
            Name = name,
            Email = normalizedEmail,
            Phone = phone,
            Username = normalizedUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            CommuneId = communeId,
            DairaId = dairaId,
            WilayaId = wilayaId,
            FailedLoginAttempts = 0,
        };

        return UserCreationResult.Success(newUser);
    }

    public async Task SaveUserAsync(User user, CancellationToken cancellationToken = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
    }
}
