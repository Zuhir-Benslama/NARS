using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Shared user creation logic used by both AdminController and AuthController.
/// Validates uniqueness, password strength, creates the User entity, and persists it.
/// </summary>
public interface IUserCreationService
{
    /// <summary>
    /// Creates a lower-tier managed account, enforcing the caller's role
    /// hierarchy and geographic scope, then validating and persisting the user.
    /// This consolidates the orchestration previously duplicated between
    /// <c>AdminUserController.CreateManagedUser</c> and
    /// <c>AuthController.AuthorizedAdminSignup</c>. The caller is only
    /// responsible for authenticating the acting user and mapping the returned
    /// status code to an HTTP response.
    /// </summary>
    Task<ManagedUserCreationResult> CreateUserAsync(
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
        CancellationToken ct = default);

    /// <summary>
    /// Validates and creates a new user. Returns a structured result with an error
    /// code the caller can map to an HTTP status. The caller is responsible for
    /// authorization checks.
    /// </summary>
    Task<UserCreationResult> ValidateAndCreateUserAsync(
        string name,
        string email,
        string phone,
        string username,
        string password,
        string role,
        int? communeId,
        int? dairaId,
        int? wilayaId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a validated user to the database.</summary>
    Task SaveUserAsync(User user, CancellationToken cancellationToken = default);
}
