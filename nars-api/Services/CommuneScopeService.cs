using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

/// <summary>
/// Determines whether a user may operate on a given commune, based on their
/// role and the geographic claims in their token. Shared by endpoints that
/// accept a caller-supplied commune id (draft features, field data) so the
/// commune→daira→wilaya hierarchy is enforced in exactly one place.
/// </summary>
public interface ICommuneScopeService
{
    /// <summary>
    /// Returns true when the caller (identified by role + geographic claims)
    /// is allowed to access the given commune. national_admin can access any
    /// commune; commune-scoped roles only their own; daira/wilaya admins any
    /// commune within their daira/wilaya.
    /// </summary>
    Task<bool> CanAccessCommuneAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        int targetCommuneId,
        CancellationToken ct = default);
}

public sealed class CommuneScopeService(IDbContextFactory<AppDbContext> dbFactory) : ICommuneScopeService
{

    public async Task<bool> CanAccessCommuneAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        int targetCommuneId,
        CancellationToken ct = default)
    {
        switch (callerRole)
        {
            case UserRoles.NationalAdmin:
                return true;

            case UserRoles.CommuneUser:
            case UserRoles.FieldWorker:
                return callerCommuneId == targetCommuneId;

            case UserRoles.DairaAdmin:
                {
                    if (!callerDairaId.HasValue)
                    {
                        return false;
                    }

                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    return await db.Communes.AnyAsync(
                        c => c.CommuneId == targetCommuneId && c.DairaId == callerDairaId.Value, ct);
                }

            case UserRoles.WilayaAdmin:
                {
                    if (!callerWilayaId.HasValue)
                    {
                        return false;
                    }

                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    return await db.Communes.AnyAsync(
                        c => c.CommuneId == targetCommuneId
                            && db.Dairas.Any(d => d.DairaId == c.DairaId && d.WilayaId == callerWilayaId.Value), ct);
                }

            default:
                return false;
        }
    }
}
