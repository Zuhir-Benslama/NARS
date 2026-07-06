using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;

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
                return Valid();
        }
    }

    private static ScopeValidationResult Valid() => new(null, false);
    private static ScopeValidationResult Error(string msg) => new(msg, false);
    private static ScopeValidationResult Forbid(string msg) => new(msg, true);
}
