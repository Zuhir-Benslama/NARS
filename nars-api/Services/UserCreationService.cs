using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class UserCreationService(
    AppDbContext db,
    IUserAuthorizationService authorizationService,
    ILogger<UserCreationService> logger)
    : IUserCreationService
{
    public async Task<ManagedUserCreationResult> CreateUserAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        string name,
        string email,
        string phone,
        string username,
        string password,
        string targetRole,
        int? communeId,
        int? dairaId,
        int? wilayaId,
        CancellationToken ct = default)
    {
        // 1. Role hierarchy.
        if (!authorizationService.CanCreateRole(callerRole, targetRole))
        {
            return ManagedUserCreationResult.Failure(
                403, $"A {callerRole} cannot create a {targetRole} account.", isAuthorizationFailure: true);
        }

        // 2. Geographic scope per role.
        var scopeResult = await authorizationService.ValidateCreateUserScopeAsync(
            callerRole, callerDairaId, callerWilayaId,
            targetRole, communeId, dairaId, wilayaId, ct);
        if (scopeResult.Error is not null)
        {
            return scopeResult.IsAuthorizationFailure
                ? ManagedUserCreationResult.Failure(403, scopeResult.Error, isAuthorizationFailure: true)
                : ManagedUserCreationResult.Failure(400, scopeResult.Error);
        }

        // 3. Resolve commune_id: field_workers inherit the creator's commune.
        var effectiveCommuneId = targetRole == UserRoles.FieldWorker ? callerCommuneId : communeId;

        // 4. Validate and create user (uniqueness, password strength, entity creation).
        var creationResult = await ValidateAndCreateUserAsync(
            name, email, phone, username, password,
            targetRole, effectiveCommuneId, dairaId, wilayaId, ct);
        if (!creationResult.IsSuccess)
        {
            var statusCode = creationResult.Code == UserCreationErrorCode.Duplicate ? 409 : 400;
            return ManagedUserCreationResult.Failure(statusCode, creationResult.Error ?? "User creation failed.");
        }

        var newUser = creationResult.User!;

        // 5. Persist (catch DB-level unique constraint races).
        try
        {
            await SaveUserAsync(newUser, ct);
        }
        catch (DbUpdateException ex)
        {
            // Sanitize line endings to prevent log forging; email is intentionally
            // omitted from the log (PII, and not needed for this diagnostic).
            logger.LogWarning(ex, "Duplicate user during account creation (username={Username})",
                username.ReplaceLineEndings(" "));
            return ManagedUserCreationResult.Failure(409, "A user with that username or email already exists.");
        }

        return ManagedUserCreationResult.Success(newUser);
    }

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

        // 1b. Email format (defense-in-depth; the HTTP layer enforces it too).
        var emailError = UserFieldValidator.ValidateEmail(email);
        if (emailError is not null)
        {
            return UserCreationResult.Failure(UserCreationErrorCode.Invalid, emailError);
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
            SecurityStamp = User.GenerateSecurityStamp(),
        };

        return UserCreationResult.Success(newUser);
    }

    public async Task SaveUserAsync(User user, CancellationToken cancellationToken = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
    }
}
