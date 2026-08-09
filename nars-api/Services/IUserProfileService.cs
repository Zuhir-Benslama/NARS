using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>Outcome of a profile/credential update attempt.</summary>
public enum CredentialUpdateError
{
    /// <summary>Update applied successfully.</summary>
    None,
    /// <summary>The target user does not exist.</summary>
    UserNotFound,
    /// <summary>The username is too long or otherwise malformed.</summary>
    InvalidUsername,
    /// <summary>The username is already in use.</summary>
    DuplicateUsername,
    /// <summary>The email address is malformed.</summary>
    InvalidEmail,
    /// <summary>The email is already in use.</summary>
    DuplicateEmail,
    /// <summary>The new password fails the strength policy.</summary>
    WeakPassword,
    /// <summary>The current password was missing or incorrect.</summary>
    WrongCurrentPassword,
}

/// <summary>
/// Result of <see cref="IUserProfileService.UpdateCredentialsAsync"/>.
/// <see cref="User"/> is populated only on success.
/// </summary>
public sealed record UpdateCredentialsResult(
    CredentialUpdateError Error = CredentialUpdateError.None,
    string? Detail = null,
    bool PasswordChanged = false,
    User? User = null)
{
    public bool Succeeded => Error == CredentialUpdateError.None;
}

public interface IUserProfileService
{
    /// <summary>Returns a user by ID, or null if not found.</summary>
    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Checks whether the given username is already in use.</summary>
    Task<bool> IsUsernameTakenAsync(string username, CancellationToken ct = default);
    /// <summary>Checks whether the given email is already in use.</summary>
    Task<bool> IsEmailTakenAsync(string email, CancellationToken ct = default);
    /// <summary>Persists changes to an existing user entity.</summary>
    Task UpdateUserAsync(User user, CancellationToken ct = default);
    /// <summary>
    /// Validates and applies a username/email/password update for the user.
    /// Returns an <see cref="UpdateCredentialsResult"/> describing the outcome;
    /// callers are responsible for post-change session handling (e.g. revoking
    /// refresh tokens) when <c>PasswordChanged</c> is true.
    /// </summary>
    Task<UpdateCredentialsResult> UpdateCredentialsAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default);
}
