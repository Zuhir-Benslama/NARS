using NarsApi.Models;

namespace NarsApi.Services;

public interface IUserProfileService
{
    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsUsernameTakenAsync(string username, CancellationToken ct = default);
    Task<bool> IsEmailTakenAsync(string email, CancellationToken ct = default);
    Task UpdateUserAsync(User user, CancellationToken ct = default);
}
