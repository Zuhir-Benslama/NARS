using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class RefreshTokenService(
    AppDbContext db,
    IJwtService jwt,
    IOptions<JwtOptions> jwtOptions,
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

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var stored = await FindRefreshTokenByHashAsync(hash, cancellationToken);

        if (stored is null)
        {
            return new RefreshTokenResult(false, "Invalid or expired refresh token.", null, null, null, null);
        }

        var user = await db.Users.FindAsync([stored.UserId], cancellationToken);
        if (user is null)
        {
            return new RefreshTokenResult(false, "User no longer exists.", null, null, null, null);
        }

        stored.Revoked = true;
        var (newRaw, newHash) = jwt.CreateRefreshToken();
        var refreshDays = jwtOptions.Value.RefreshExpiresInDays;
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

        return new RefreshTokenResult(true, null, user.Username, newRaw, newAccessToken, refreshExpiry);
    }

    protected virtual Task<RefreshToken?> FindRefreshTokenByHashAsync(string hash, CancellationToken ct)
    {
        var tableName = db.Model.FindEntityType(typeof(RefreshToken))?.GetTableName() ?? "refresh_tokens";
        return db.RefreshTokens
            .FromSqlInterpolated(
                $"SELECT * FROM {tableName} WHERE token_hash = {hash} AND revoked = false AND expires_at > NOW() FOR UPDATE SKIP LOCKED")
            .FirstOrDefaultAsync(ct);
    }
}
