using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Shared user creation logic used by both AdminController and AuthController.
/// Validates uniqueness, password strength, creates the User entity, and persists it.
/// </summary>
public interface IUserCreationService
{
    /// <summary>
    /// Validates and creates a new user. Returns null on success, or an error string.
    /// The caller is responsible for authorization checks.
    /// </summary>
    Task<(User? User, string? Error)> ValidateAndCreateUserAsync(
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
