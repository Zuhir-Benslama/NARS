using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Shared user creation logic used by both AdminController and AuthController.
/// Validates uniqueness, password strength, creates the User entity, and persists it.
/// </summary>
public interface IUserCreationService
{
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
