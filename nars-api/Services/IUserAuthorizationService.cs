using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Services;

public record ScopeValidationResult(string? Error, bool IsAuthorizationFailure);

public interface IUserAuthorizationService
{
    /// <summary>Checks whether a caller role is allowed to create a target role.</summary>
    bool CanCreateRole(string callerRole, string targetRole);
    /// <summary>Validates the geographic scope when creating a lower-tier account.</summary>
    Task<ScopeValidationResult> ValidateCreateUserScopeAsync(
        string callerRole, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default);
    /// <summary>Returns users that the caller has authority to manage.</summary>
    Task<List<AdminUserSummary>> GetManageableUsersAsync(
        string callerRole, Guid callerUserId, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default);
    /// <summary>Finds a user by ID.</summary>
    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Checks whether an email is already taken by another user.</summary>
    Task<bool> IsEmailTakenAsync(string email, Guid excludeUserId, CancellationToken ct = default);
    /// <summary>Deletes a user account.</summary>
    Task<bool> DeleteUserAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Saves all pending changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
    /// <summary>Persists a new user to the database.</summary>
    Task AddUserAsync(User user, CancellationToken ct = default);
}
