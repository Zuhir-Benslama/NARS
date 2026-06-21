using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public record RefreshTokenResult(
    bool Success,
    string? Detail,
    User? User,
    string? NewRawToken,
    string? NewAccessToken,
    DateTime? RefreshExpiry
);

public class RefreshTokenService(
    AppDbContext db,
    IJwtService jwt,
    IConfiguration config,
    IDateTimeProvider timeProvider) : IRefreshTokenService
{
    public async Task<RefreshTokenResult> RotateRefreshTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rawRefreshToken))
        {
            return new RefreshTokenResult(false, "No refresh token.", null, null, null, null);
        }

        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawRefreshToken)));

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var stored = await db.RefreshTokens
            .FromSqlRaw(
                "SELECT * FROM refresh_tokens WHERE token_hash = {0} AND revoked = false AND expires_at > NOW() FOR UPDATE SKIP LOCKED",
                hash)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new RefreshTokenResult(false, "Invalid or expired refresh token.", null, null, null, null);
        }

        var user = await db.Users.FindAsync([stored.UserId], cancellationToken);
        if (user is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new RefreshTokenResult(false, "User no longer exists.", null, null, null, null);
        }

        stored.Revoked = true;
        var (newRaw, newHash) = jwt.CreateRefreshToken();
        var refreshDays = int.TryParse(config["Jwt:RefreshExpiresInDays"], out var d) ? d : 30;
        var refreshExpiry = timeProvider.UtcNow.AddDays(refreshDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            ExpiresAt = refreshExpiry,
        });

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var newAccessToken = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email,
            communeId: user.CommuneId, role: user.Role, dairaId: user.DairaId, wilayaId: user.WilayaId);

        return new RefreshTokenResult(true, null, user, newRaw, newAccessToken, refreshExpiry);
    }
}
