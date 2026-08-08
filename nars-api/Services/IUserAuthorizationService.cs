using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Services;

public record ScopeValidationResult(string? Error, bool IsAuthorizationFailure);

/// <summary>Outcome of a credential verification attempt.</summary>
public enum CredentialCheckStatus
{
    Success,
    InvalidCredentials,
    Locked,
}

/// <summary>
/// Structured result for <see cref="IUserAuthorizationService.VerifyCredentialsAsync"/>.
/// Carries the authenticated user on success so callers can distinguish an
/// invalid-credentials failure from a locked account.
/// </summary>
public record CredentialCheckResult(User? User, CredentialCheckStatus Status)
{
    public bool IsSuccess => Status == CredentialCheckStatus.Success;

    public static CredentialCheckResult Success(User user) => new(user, CredentialCheckStatus.Success);
    public static CredentialCheckResult Invalid() => new(null, CredentialCheckStatus.InvalidCredentials);
    public static CredentialCheckResult Locked() => new(null, CredentialCheckStatus.Locked);
}

public interface IUserAuthorizationService
{
    /// <summary>Checks whether a caller role is allowed to create a target role.</summary>
    bool CanCreateRole(string callerRole, string targetRole);
    /// <summary>Validates the geographic scope when creating a lower-tier account.</summary>
    Task<ScopeValidationResult> ValidateCreateUserScopeAsync(
        string callerRole, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default);
    /// <summary>
    /// Validates that a target user's role + geography lies within the caller's
    /// management scope. Used for update and delete paths, where the target's
    /// current (or effective) role/geography must belong to the caller's scope.
    /// </summary>
    Task<ScopeValidationResult> ValidateManagedUserScopeAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default);
    /// <summary>Returns the users the caller can manage, wrapped in a paged envelope.</summary>
    Task<PagedResponse<AdminUserSummary>> GetManageableUsersAsync(
        string callerRole, int? communeId, int? dairaId, int? wilayaId,
        int skip = 0, int take = 100,
        CancellationToken ct = default);
    /// <summary>Finds a user by ID.</summary>
    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Finds a user by (normalized, lowercased) username.</summary>
    Task<User?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken ct = default);
    /// <summary>
    /// Verifies a username/password pair with constant-time timing protection for
    /// unknown usernames, records failed logins on mismatches, and reports lockout.
    /// Consolidates the credential check previously duplicated between
    /// <c>AuthController.SignIn</c> and <c>AuthController.AuthorizedAdminSignup</c>.
    /// </summary>
    Task<CredentialCheckResult> VerifyCredentialsAsync(
        string normalizedUsername, string password, int maxFailedAttempts, int lockoutMinutes,
        CancellationToken ct = default);
    /// <summary>
    /// Updates a managed user's profile, role, or geographic scope, enforcing the
    /// caller's role hierarchy and geographic scope. Returns a structured result
    /// the caller can map to an HTTP status.
    /// </summary>
    Task<UserUpdateResult> UpdateManagedUserAsync(
        Guid callerUserId, string callerRole,
        int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid targetUserId, UpdateAdminRequest body,
        CancellationToken ct = default);
    /// <summary>Deletes a user account.</summary>
    Task<bool> DeleteUserAsync(Guid userId, CancellationToken ct = default);
}
