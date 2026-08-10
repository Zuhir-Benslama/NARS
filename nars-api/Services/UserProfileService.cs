using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class UserProfileService(AppDbContext db) : IUserProfileService
{
    public async Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default) =>
        await db.Users.FindAsync([userId], ct);

    public Task<bool> IsUsernameTakenAsync(string username, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Username == username, ct);

    public Task<bool> IsEmailTakenAsync(string email, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task UpdateUserAsync(User user, CancellationToken ct = default) => await db.SaveChangesAsync(ct);

    public async Task<UpdateCredentialsResult> UpdateCredentialsAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await GetUserByIdAsync(userId, ct);
        if (user is null)
        {
            return new UpdateCredentialsResult(CredentialUpdateError.UserNotFound);
        }

        // Validate username uniqueness if changed (store normalized lowercase).
        if (!string.IsNullOrWhiteSpace(request.Username))
        {
            var lengthError = UserFieldValidator.ValidateMaxLength(request.Username, UserFieldValidator.MaxUsernameLength, "Username");
            if (lengthError is not null)
            {
                return new UpdateCredentialsResult(CredentialUpdateError.InvalidUsername, Detail: lengthError);
            }

            var normalized = request.Username.Trim().ToLowerInvariant();
            if (normalized != user.Username)
            {
                if (await IsUsernameTakenAsync(normalized, ct))
                {
                    return new UpdateCredentialsResult(CredentialUpdateError.DuplicateUsername);
                }

                user.Username = normalized;
            }
        }

        // Validate email uniqueness if changed (store normalized lowercase, matching creation).
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var emailError = UserFieldValidator.ValidateEmail(request.Email);
            if (emailError is not null)
            {
                return new UpdateCredentialsResult(CredentialUpdateError.InvalidEmail, Detail: emailError);
            }

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            if (normalizedEmail != user.Email)
            {
                if (await IsEmailTakenAsync(normalizedEmail, ct))
                {
                    return new UpdateCredentialsResult(CredentialUpdateError.DuplicateEmail);
                }

                user.Email = normalizedEmail;
            }
        }

        // Only update the password when explicitly provided and valid.
        // Never hash an empty string — that would lock the user out.
        var passwordChanged = false;
        if (!string.IsNullOrEmpty(request.Password))
        {
            // Require the current password so a stolen session cookie alone
            // cannot change credentials (defense against account takeover).
            if (string.IsNullOrEmpty(request.CurrentPassword)
                || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                return new UpdateCredentialsResult(CredentialUpdateError.WrongCurrentPassword);
            }

            var pwdErr = PasswordValidator.Validate(request.Password);
            if (pwdErr is not null)
            {
                return new UpdateCredentialsResult(CredentialUpdateError.WeakPassword, Detail: pwdErr);
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            passwordChanged = true;
            // Rotate the security stamp so access tokens issued before the
            // password change are rejected immediately.
            user.SecurityStamp = User.GenerateSecurityStamp();
        }

        try
        {
            await UpdateUserAsync(user, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var constraintName))
        {
            // TOCTOU race: a concurrent request claimed the username/email
            // first. Match the colliding unique constraint so we return the
            // correct error instead of always reporting a duplicate username.
            return constraintName?.Contains("email", StringComparison.OrdinalIgnoreCase) == true
                ? new UpdateCredentialsResult(CredentialUpdateError.DuplicateEmail)
                : new UpdateCredentialsResult(CredentialUpdateError.DuplicateUsername);
        }

        return new UpdateCredentialsResult(PasswordChanged: passwordChanged, User: user);
    }

    /// <summary>
    /// Returns true when <paramref name="ex"/> chains to a PostgreSQL unique
    /// violation (SQLSTATE 23505), exposing the colliding constraint name.
    /// Genuine server failures (connection loss, deadlock, disk) fall through
    /// to the caller as unhandled DbUpdateException instead of being masked.
    /// </summary>
    internal static bool IsUniqueViolation(DbUpdateException ex, out string? constraintName)
    {
        constraintName = null;
        for (Exception? inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is Npgsql.PostgresException pg && pg.SqlState == "23505")
            {
                constraintName = pg.ConstraintName;
                return true;
            }
        }
        return false;
    }
}
