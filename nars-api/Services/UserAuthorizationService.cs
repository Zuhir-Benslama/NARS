using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class UserAuthorizationService(AppDbContext db) : IUserAuthorizationService
{
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

    private Task<ScopeValidationResult> ValidateUpdateUserScopeAsync(
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
                    return Forbid("Field workers must remain in your commune.");
                }

                return Valid();

            case (UserRoles.DairaAdmin, UserRoles.CommuneUser):
                if (!communeId.HasValue)
                {
                    return Error("commune_id is required for commune_user.");
                }

                var commune = await db.Communes.FindAsync([communeId.Value], ct);
                if (commune is null)
                {
                    return Error("Commune not found.");
                }

                if (commune.DairaId != callerDairaId)
                {
                    return Forbid("That commune does not belong to your daira.");
                }

                return Valid();

            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                if (!dairaId.HasValue)
                {
                    return Error("daira_id is required for daira_admin.");
                }

                var daira = await db.Dairas.FindAsync([dairaId.Value], ct);
                if (daira is null)
                {
                    return Error("Daira not found.");
                }

                if (daira.WilayaId != callerWilayaId)
                {
                    return Forbid("That daira does not belong to your wilaya.");
                }

                return Valid();

            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (!wilayaId.HasValue)
                {
                    return Error("wilaya_id is required for wilaya_admin.");
                }

                return Valid();

            default:
                return Error($"Unsupported role transition ({callerRole} → {targetRole}).");
        }
    }

    public async Task<List<AdminUserSummary>> GetManageableUsersAsync(
        string callerRole, Guid callerUserId, int? communeId, int? dairaId, int? wilayaId,
        int skip = 0, int take = 100,
        CancellationToken ct = default) => callerRole switch
        {
            UserRoles.NationalAdmin => await db.Users
                .Where(u => u.Role == UserRoles.WilayaAdmin)
                .OrderBy(u => u.Username)
                .Skip(skip).Take(take)
                .Select(u => new AdminUserSummary(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role, u.Phone ?? "", u.CommuneId, u.DairaId, u.WilayaId))
                .ToListAsync(ct),

            UserRoles.WilayaAdmin when wilayaId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId.HasValue)
                .Join(db.Dairas.Where(d => d.WilayaId == wilayaId.Value),
                    u => u.DairaId!.Value, d => d.DairaId, (u, _) => u)
                .OrderBy(u => u.Username)
                .Skip(skip).Take(take)
                .Select(u => new AdminUserSummary(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role, u.Phone ?? "", u.CommuneId, u.DairaId, u.WilayaId))
                .ToListAsync(ct),

            UserRoles.DairaAdmin when dairaId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue)
                .Join(db.Communes.Where(c => c.DairaId == dairaId.Value),
                    u => u.CommuneId!.Value, c => c.CommuneId, (u, _) => u)
                .OrderBy(u => u.Username)
                .Skip(skip).Take(take)
                .Select(u => new AdminUserSummary(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role, u.Phone ?? "", u.CommuneId, u.DairaId, u.WilayaId))
                .ToListAsync(ct),

            UserRoles.CommuneUser when communeId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.FieldWorker && u.CommuneId == communeId)
                .OrderBy(u => u.Username)
                .Skip(skip).Take(take)
                .Select(u => new AdminUserSummary(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role, u.Phone ?? "", u.CommuneId, u.DairaId, u.WilayaId))
                .ToListAsync(ct),

            _ => [],
        };

    public async Task<User?> FindUserByIdAsync(Guid userId, CancellationToken ct = default)
        => await db.Users.FindAsync([userId], ct);

    public async Task<User?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken ct = default)
        => await db.Users.FirstOrDefaultAsync(u => u.Username == normalizedUsername, ct);

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
        if (sensitiveChange)
        {
            if (string.IsNullOrEmpty(body.Password))
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.PasswordRequired,
                    "Password is required to change role or geographic scope.");
            }

            var caller = await db.Users.FindAsync([callerUserId], ct);
            if (caller is null || !BCrypt.Net.BCrypt.Verify(body.Password, caller.PasswordHash))
            {
                return UserUpdateResult.Failure(UserUpdateErrorCode.InvalidPassword, "Password is incorrect.");
            }

            // Geographic scope: the target's effective role + geography (existing
            // values merged with the requested changes) must stay within the
            // caller's scope.
            var effectiveRole = body.Role ?? target.Role;
            var effectiveCommuneId = body.CommuneId ?? target.CommuneId;
            var effectiveDairaId = body.DairaId ?? target.DairaId;
            var effectiveWilayaId = body.WilayaId ?? target.WilayaId;

            var scopeResult = await ValidateUpdateUserScopeAsync(
                callerRole, callerCommuneId, callerDairaId, callerWilayaId,
                effectiveRole, effectiveCommuneId, effectiveDairaId, effectiveWilayaId, ct);
            if (scopeResult.Error is not null)
            {
                return scopeResult.IsAuthorizationFailure
                    ? UserUpdateResult.Failure(UserUpdateErrorCode.Forbidden, scopeResult.Error)
                    : UserUpdateResult.Failure(UserUpdateErrorCode.Invalid, scopeResult.Error);
            }
        }

        // Apply profile fields.
        if (body.Name is not null)
        {
            target.Name = body.Name;
        }

        if (body.Email is not null)
        {
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
            target.Phone = body.Phone;
        }

        // Apply role + geography.
        if (body.Role is not null)
        {
            var geoCheck = GeographicValidator.Validate(body.Role, body.CommuneId, body.DairaId, body.WilayaId);
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

        await db.SaveChangesAsync(ct);
        return UserUpdateResult.Success();
    }

    public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return false;
        }

        // Revoke all active refresh tokens.
        var tokens = await db.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.Revoked)
            .ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.Revoked = true;
        }

        // Delete all features across all feature tables and collect their IDs.
        var deletedFeatureIds = new List<Guid>();
        foreach (var descriptor in FeatureTypeRegistry.GetAllDescriptors())
        {
            var dbSet = descriptor.GetDbSet(db);
            var entities = await dbSet.Where(f => f.UserId == userId).ToListAsync(ct);
            if (entities.Count > 0)
            {
                deletedFeatureIds.AddRange(entities.Select(e => e.Id));
                db.RemoveRange(entities);
            }
        }

        if (deletedFeatureIds.Count > 0)
        {
            var registryEntries = await db.FeatureRegistry.Where(r => deletedFeatureIds.Contains(r.Id)).ToListAsync(ct);
            db.FeatureRegistry.RemoveRange(registryEntries);
        }

        // Delete inspections and error logs.
        var inspections = await db.Inspections.Where(i => i.UserId == userId).ToListAsync(ct);
        db.Inspections.RemoveRange(inspections);

        var errorLogs = await db.ErrorLogs.Where(el => el.UserId == userId).ToListAsync(ct);
        db.ErrorLogs.RemoveRange(errorLogs);

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static ScopeValidationResult Valid() => new(null, false);
    private static ScopeValidationResult Error(string msg) => new(msg, false);
    private static ScopeValidationResult Forbid(string msg) => new(msg, true);
}
