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

    public virtual async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await db.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.Revoked)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(rt => rt.Revoked, true), cancellationToken);
    }

    public async Task<(string RawToken, string Hash, DateTime ExpiresAt)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (rawToken, hash) = jwt.CreateRefreshToken();
        var expiresAt = timeProvider.UtcNow.AddDays(jwtOptions.Value.RefreshExpiresInDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        return (rawToken, hash, expiresAt);
    }

    public async Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await db.Users.FindAsync([userId], cancellationToken);

    public async Task<User?> FindUserByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default)
        => await db.Users.FirstOrDefaultAsync(u => u.Username == normalizedUsername, cancellationToken);

    public async Task AddUserAsync(User user, CancellationToken cancellationToken = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailedLoginAsync(User user, int maxFailedAttempts, int lockoutMinutes, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        user.FailedLoginAttempts = (user.FailedLoginAttempts ?? 0) + 1;
        if (user.FailedLoginAttempts >= maxFailedAttempts)
        {
            user.LockedUntil = utcNow.UtcDateTime.AddMinutes(lockoutMinutes);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetFailedAttemptsIfNeededAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.FailedLoginAttempts > 0 || user.LockedUntil.HasValue)
        {
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    protected virtual Task<RefreshToken?> FindRefreshTokenByHashAsync(string hash, CancellationToken ct)
    {
        const string expectedTable = "refresh_tokens";
        var tableName = db.Model.FindEntityType(typeof(RefreshToken))?.GetTableName();
        if (!string.Equals(tableName, expectedTable, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unexpected table name '{tableName}' for RefreshToken entity.");
        }

        return db.RefreshTokens
            .FromSqlRaw(
                $"SELECT * FROM {expectedTable} WHERE token_hash = {{0}} AND revoked = false AND expires_at > NOW() FOR UPDATE SKIP LOCKED",
                hash)
            .FirstOrDefaultAsync(ct);
    }
}
