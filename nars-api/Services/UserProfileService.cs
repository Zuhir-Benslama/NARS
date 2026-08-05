using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class UserProfileService(AppDbContext db) : IUserProfileService
{
    public async Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default) =>
        await db.Users.FindAsync([userId], ct);

    public Task<bool> IsUsernameTakenAsync(string username, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Username == username, ct);

    public Task<bool> IsEmailTakenAsync(string email, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task UpdateUserAsync(User user, CancellationToken ct = default) => await db.SaveChangesAsync(ct);
}
