using System.Data;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class UserAuthorizationService(
    AppDbContext db,
    IRefreshTokenService refreshService,
    IAccountLockoutService accountLockout,
    IFeatureCleanupService cleanupService,
    IDateTimeProvider timeProvider,
    ISecurityStampCache stampCache) : IUserAuthorizationService
{
    // Stable dummy hash so BCrypt always does the full work, even for unknown users.
    // Prevents username enumeration via response-time side-channel.
    //
    // DELIBERATE TRADE-OFF (kept): running the full BCrypt cost for unknown
    // usernames costs ~300ms CPU per attempt, which is a DoS surface for any
    // request reaching VerifyCredentialsAsync. Making the dummy path cheap (e.g. a
    // CryptographicOperations.FixedTimeEquals against a constant) would re-open a
    // ~300ms timing side channel that enumerates registered usernames — the exact
    // weakness the dummy hash exists to close. The CPU burn is bounded instead by
    // RateLimitPolicies.Auth (sliding window, default 5 requests / 30s, zero queue)
    // on the only two anonymous entry points (signin + authorized-signup), and the
    // signup path requires the X-Admin-Signup header. Do not replace this dummy
    // verify with a cheap comparison without also rate-limiting the caller harder.
    private const string DummyHash = "$2a$11$BCfJgwy.hTY703/9RBjPo.8UjBrTHh/95zFznkYLiapLvWdf5ISbO";

    public bool CanCreateRole(string callerRole, string targetRole) => (callerRole, targetRole) switch
    {
        (UserRoles.NationalAdmin, UserRoles.WilayaAdmin) => true,
        (UserRoles.WilayaAdmin, UserRoles.DairaAdmin) => true,
        (UserRoles.DairaAdmin, UserRoles.CommuneUser) => true,
        (UserRoles.CommuneUser, UserRoles.FieldWorker) => true,
        _ => false,
    };

    public Task<ScopeValidationResult> ValidateCreateUserScopeAsync(
        string callerRole, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default)
        => ValidateScopeAsync(callerRole, callerCommuneId: null, callerDairaId, callerWilayaId,
            targetRole, communeId, dairaId, wilayaId, requireExactFieldWorkerCommune: false, ct);

    public Task<ScopeValidationResult> ValidateManagedUserScopeAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default)
        => ValidateScopeAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId,
            targetRole, communeId, dairaId, wilayaId, requireExactFieldWorkerCommune: true, ct);

    private async Task<ScopeValidationResult> ValidateScopeAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        bool requireExactFieldWorkerCommune, CancellationToken ct)
    {
        switch (callerRole, targetRole)
        {
            case (UserRoles.CommuneUser, UserRoles.FieldWorker):
                if (requireExactFieldWorkerCommune && communeId != callerCommuneId)
                {
                    return AuthorizationDenied("Field workers must remain in your commune.");
                }

                return Valid();

            case (UserRoles.DairaAdmin, UserRoles.CommuneUser):
                if (!communeId.HasValue)
                {
                    return ValidationError("commune_id is required for commune_user.");
                }

                var commune = await db.Communes.FindAsync([communeId.Value], ct);
                if (commune is null)
                {
                    return ValidationError("Commune not found.");
                }

                if (commune.DairaId != callerDairaId)
                {
                    return AuthorizationDenied("That commune does not belong to your daira.");
                }

                return Valid();

            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                if (!dairaId.HasValue)
                {
                    return ValidationError("daira_id is required for daira_admin.");
                }

                var daira = await db.Dairas.FindAsync([dairaId.Value], ct);
                if (daira is null)
                {
                    return ValidationError("Daira not found.");
                }

                if (daira.WilayaId != callerWilayaId)
                {
                    return AuthorizationDenied("That daira does not belong to your wilaya.");
                }

                return Valid();

            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (!wilayaId.HasValue)
                {
                    return ValidationError("wilaya_id is required for wilaya_admin.");
                }

                return Valid();

            default:
                return ValidationError($"Unsupported role transition ({callerRole} → {targetRole}).");
        }
    }

    public async Task<PagedResponse<AdminUserSummary>> GetManageableUsersAsync(
        string callerRole, int? communeId, int? dairaId, int? wilayaId,
        int skip = 0, int take = 100,
        CancellationToken ct = default)
    {
        IQueryable<User>? baseQuery = callerRole switch
        {
            UserRoles.NationalAdmin => db.Users.Where(u => u.Role == UserRoles.WilayaAdmin),
            UserRoles.WilayaAdmin when wilayaId.HasValue => db.Users
                .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId.HasValue)
                .Join(db.Dairas.Where(d => d.WilayaId == wilayaId.Value),
                    u => u.DairaId!.Value, d => d.DairaId, (u, _) => u),
            UserRoles.DairaAdmin when dairaId.HasValue => db.Users
                .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue)
                .Join(db.Communes.Where(c => c.DairaId == dairaId.Value),
                    u => u.CommuneId!.Value, c => c.CommuneId, (u, _) => u),
            UserRoles.CommuneUser when communeId.HasValue => db.Users
                .Where(u => u.Role == UserRoles.FieldWorker && u.CommuneId == communeId),
            _ => null,
        };

        if (baseQuery is null)
        {
            return new PagedResponse<AdminUserSummary>([], 0, skip, take);
        }

        var ordered = baseQuery.OrderBy(u => u.Username);
        var total = await ordered.CountAsync(ct);
        var items = await ordered
            .Skip(skip).Take(take)
            .Select(u => new AdminUserSummary(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role, u.Phone ?? "", u.CommuneId, u.DairaId, u.WilayaId))
            .ToListAsync(ct);

        return new PagedResponse<AdminUserSummary>(items, total, skip, take);
    }

    // Read-only lookups (profile response, delete authorization) never mutate
    // through the tracker, so skip change-tracking overhead.
    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken ct = default)
        => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

    public async Task<User?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken ct = default)
        => await db.Users.FirstOrDefaultAsync(u => u.Username == normalizedUsername, ct);

    public async Task<CredentialCheckResult> VerifyCredentialsAsync(
        string normalizedUsername, string password, int maxFailedAttempts, int lockoutMinutes,
        CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == normalizedUsername, ct);

        // Always run BCrypt.Verify even when the user is not found.
        // Short-circuiting on "user is null" leaks whether a username exists
        // via response-time difference (~0 µs vs ~300 ms for a real BCrypt check).
        var hashToCheck = user?.PasswordHash ?? DummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(password, hashToCheck);

        // Lockout check runs after BCrypt to preserve timing-attack resistance.
        // It must also run BEFORE recording a failure: once an account is
        // locked, further wrong passwords are ignored entirely, so the lockout
        // always runs its fixed course instead of being extendable forever by
        // anyone who knows the username.
        if (user is not null && user.LockedUntil.HasValue && user.LockedUntil > timeProvider.UtcNow)
        {
            return CredentialCheckResult.Locked();
        }

        // Only record failed logins for actual password mismatches on
        // non-locked accounts (see above).
        if (user is not null && !passwordValid)
        {
            await accountLockout.RecordFailedLoginAsync(user, maxFailedAttempts, lockoutMinutes, timeProvider.UtcNow, ct);
        }

        if (user is null || !passwordValid)
        {
            return CredentialCheckResult.Invalid();
        }

        return CredentialCheckResult.Success(user);
    }

    public async Task<UserUpdateResult> UpdateManagedUserAsync(
        Guid callerUserId, string callerRole,
        int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid targetUserId, UpdateAdminRequest body,
        CancellationToken ct = default)
    {
        var target = await db.Users.FindAsync([targetUserId], ct);
        if (target is null)
        {
            return UserUpdateResult.Failure(UserUpdateErrorCode.NotFound, "User not found.");
        }

        // Role hierarchy: the caller must be able to manage both the target's
        // current role and any requested role transition.
        if (!CanCreateRole(callerRole, target.Role))
        {
            return UserUpdateResult.Failure(UserUpdateErrorCode.Forbidden);
        }

        if (body.Role is not null && !CanCreateRole(callerRole, body.Role))
        {
            return UserUpdateResult.Failure(UserUpdateErrorCode.Forbidden);
        }

        var sensitiveChange = body.Role is not null
            || body.WilayaId is not null
            || body.DairaId is not null
            || body.CommuneId is not null;
        if (sensitiveChange && string.IsNullOrEmpty(body.Password))
        {
            return UserUpdateResult.Failure(UserUpdateErrorCode.PasswordRequired,
                "Password is required to change role or geographic scope.");
        }

        if (sensitiveChange)
        {
            var caller = await db.Users.FindAsync([callerUserId], ct);
            if (caller is null || !BCrypt.Net.BCrypt.Verify(body.Password, caller.PasswordHash))
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.InvalidPassword, "Password is incorrect.");
            }
        }

        // Geographic scope: the target's effective role + geography (existing
        // values merged with the requested changes) must stay within the
        // caller's scope. Enforced on every update, including profile-only
        // edits, so a caller cannot tamper with users outside its scope.
        var effectiveRole = body.Role ?? target.Role;
        var effectiveCommuneId = body.CommuneId ?? target.CommuneId;
        var effectiveDairaId = body.DairaId ?? target.DairaId;
        var effectiveWilayaId = body.WilayaId ?? target.WilayaId;

        var scopeResult = await ValidateManagedUserScopeAsync(
            callerRole, callerCommuneId, callerDairaId, callerWilayaId,
            effectiveRole, effectiveCommuneId, effectiveDairaId, effectiveWilayaId, ct);
        if (scopeResult.Error is not null)
        {
            return scopeResult.IsAuthorizationFailure
                ? UserUpdateResult.Failure(UserUpdateErrorCode.Forbidden, scopeResult.Error)
                : UserUpdateResult.Failure(UserUpdateErrorCode.Invalid, scopeResult.Error);
        }

        // Apply profile fields.
        if (body.Name is not null)
        {
            var nameError = UserFieldValidator.ValidateMaxLength(body.Name, UserFieldValidator.MaxNameLength, "Name");
            if (nameError is not null)
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.Invalid, nameError);
            }

            target.Name = body.Name;
        }

        if (body.Email is not null)
        {
            var emailError = UserFieldValidator.ValidateEmail(body.Email);
            if (emailError is not null)
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.Invalid, emailError);
            }

            var normalizedEmail = body.Email.ToLowerInvariant();
            var emailConflict = await db.Users.AnyAsync(u => u.Email == normalizedEmail && u.Id != target.Id, ct);
            if (emailConflict)
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.EmailConflict, "Email already exists.");
            }

            target.Email = normalizedEmail;
        }

        if (body.Phone is not null)
        {
            var phoneError = UserFieldValidator.ValidateMaxLength(body.Phone, UserFieldValidator.MaxPhoneLength, "Phone");
            if (phoneError is not null)
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.Invalid, phoneError);
            }

            target.Phone = body.Phone;
        }

        // Apply role + geography. Capture the pre-update privileges so we can
        // detect whether anything that feeds access-token claims actually changed.
        var originalRole = target.Role;
        var originalWilayaId = target.WilayaId;
        var originalDairaId = target.DairaId;
        var originalCommuneId = target.CommuneId;

        if (body.Role is not null)
        {
            // Validate against the merged effective values (not the raw body), so
            // the check reflects the full post-update geography and agrees with the
            // effective-scope validation performed above.
            var geoCheck = GeographicValidator.Validate(body.Role, effectiveCommuneId, effectiveDairaId, effectiveWilayaId);
            if (geoCheck is not null)
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.Invalid, geoCheck);
            }

            target.Role = body.Role;
        }

        if (body.WilayaId is not null)
        {
            target.WilayaId = body.WilayaId;
        }

        if (body.DairaId is not null)
        {
            target.DairaId = body.DairaId;
        }

        if (body.CommuneId is not null)
        {
            target.CommuneId = body.CommuneId;
        }

        // A privilege change (role or geographic scope) must invalidate the
        // target's existing sessions: rotate the security stamp so outstanding
        // access tokens carrying the old claims are rejected on their next
        // request, and revoke refresh tokens so the session cannot be renewed.
        // The caller already re-authenticated with their password above.
        var privilegesChanged = target.Role != originalRole
            || target.WilayaId != originalWilayaId
            || target.DairaId != originalDairaId
            || target.CommuneId != originalCommuneId;
        if (privilegesChanged)
        {
            target.SecurityStamp = User.GenerateSecurityStamp();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (UserProfileService.IsUniqueViolation(ex, out var constraintName))
        {
            // TOCTOU race: a concurrent request claimed the email between the
            // AnyAsync check above and this save. Surface it as the same 409
            // the eager check returns instead of an unhandled 500.
            return constraintName?.Contains("email", StringComparison.OrdinalIgnoreCase) == true
                ? UserUpdateResult.Failure(UserUpdateErrorCode.EmailConflict, "Email already exists.")
                : UserUpdateResult.Failure(UserUpdateErrorCode.EmailConflict, "User already exists.");
        }

        if (privilegesChanged)
        {
            stampCache.EvictStamp(target.Id);
            await refreshService.RevokeAllUserTokensAsync(target.Id, ct);
        }

        return UserUpdateResult.Success();
    }

    public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return false;
        }

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // Feature tables have no FK/cascade to users (by design), so their rows
        // and feature_registry entries are removed explicitly. Refresh tokens,
        // inspections, and error logs are cleaned up by the ON DELETE CASCADE
        // relationships declared in AppDbContext.
        await cleanupService.DeleteAllFeaturesForUserAsync(db, userId, ct);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private static ScopeValidationResult Valid() => new(null, false);
    private static ScopeValidationResult ValidationError(string msg) => new(msg, false);
    private static ScopeValidationResult AuthorizationDenied(string msg) => new(msg, true);
}
