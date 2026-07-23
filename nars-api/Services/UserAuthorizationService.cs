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

    public async Task<ScopeValidationResult> ValidateCreateUserScopeAsync(
        string callerRole, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default)
    {
        switch (callerRole, targetRole)
        {
            case (UserRoles.CommuneUser, UserRoles.FieldWorker):
                return Valid();

            case (UserRoles.DairaAdmin, UserRoles.CommuneUser):
                if (!communeId.HasValue)
                    return Error("commune_id is required when creating a commune_user.");
                var commune = await db.Communes.FindAsync([communeId.Value], ct);
                if (commune is null)
                    return Error("Commune not found.");
                if (commune.DairaId != callerDairaId)
                    return Forbid("That commune does not belong to your daira.");
                return Valid();

            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                if (!dairaId.HasValue)
                    return Error("daira_id is required when creating a daira_admin.");
                var daira = await db.Dairas.FindAsync([dairaId.Value], ct);
                if (daira is null)
                    return Error("Daira not found.");
                if (daira.WilayaId != callerWilayaId)
                    return Forbid("That daira does not belong to your wilaya.");
                return Valid();

            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (!wilayaId.HasValue)
                    return Error("wilaya_id is required when creating a wilaya_admin.");
                return Valid();

            default:
                return Error($"Unsupported role transition ({callerRole} → {targetRole}).");
        }
    }

    public async Task<List<AdminUserSummary>> GetManageableUsersAsync(
        string callerRole, Guid callerUserId, int? communeId, int? dairaId, int? wilayaId,
        int skip = 0, int take = 100,
        CancellationToken ct = default)
    {
        return callerRole switch
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
    }

    public async Task<User?> FindUserByIdAsync(Guid userId, CancellationToken ct = default)
        => await db.Users.FindAsync([userId], ct);

    public async Task<bool> IsEmailTakenAsync(string email, Guid excludeUserId, CancellationToken ct = default)
        => await db.Users.AnyAsync(u => u.Email == email && u.Id != excludeUserId, ct);

    public async Task<bool> DeleteUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return false;

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await db.SaveChangesAsync(ct);

    public async Task AddUserAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    private static ScopeValidationResult Valid() => new(null, false);
    private static ScopeValidationResult Error(string msg) => new(msg, false);
    private static ScopeValidationResult Forbid(string msg) => new(msg, true);
}
