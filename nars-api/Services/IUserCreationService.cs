using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Shared user creation logic used by both AdminController and AuthController.
/// Validates uniqueness, password strength, and creates the User entity.
/// </summary>
public interface IUserCreationService
{
    /// <summary>
    /// Validates and creates a new user. Returns null on success, or an error string.
    /// The caller is responsible for authorization checks and saving to the database.
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
}
