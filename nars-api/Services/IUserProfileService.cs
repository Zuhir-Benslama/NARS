using NarsApi.Models;

namespace NarsApi.Services;

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
}
