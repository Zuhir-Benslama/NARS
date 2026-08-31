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
    ISecurityStampCache stampCache,
    IDateTimeProvider timeProvider) : IRefreshTokenService
{
    protected IDateTimeProvider TimeProvider => timeProvider;

    /// <summary>
    /// Produces the base64-encoded SHA-256 hash of a raw refresh token string.
    /// </summary>
    private static string HashRefreshToken(string rawRefreshToken) =>
        Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(rawRefreshToken)));

    public async Task<RefreshTokenResult> RotateRefreshTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rawRefreshToken))
        {
            return new RefreshTokenResult(false, "No refresh token.", null, null, null, null);
        }

        var hash = HashRefreshToken(rawRefreshToken);

        // ReadCommitted is sufficient here because the SELECT ... FOR UPDATE SKIP LOCKED
        // in FindRefreshTokenByHashAsync already serializes concurrent rotation attempts:
        // two concurrent refresh requests will each lock a different row (or skip if locked),
        // preventing double-spend of the same refresh token.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var stored = await FindRefreshTokenByHashAsync(hash, cancellationToken);

        if (stored is null)
        {
            return new RefreshTokenResult(false, "Invalid or expired refresh token.", null, null, null, null);
        }

        // Replay of an already-rotated token can mean one of two things:
        //   * a benign retry by the legitimate client that just rotated the
        //     token moments ago (double-click, two tabs, UA retry), or
        //   * a genuine replay of a stolen token much later by an attacker.
        // Only the second signals theft: revoke every outstanding token so the
        // attacker's session dies too, then reject the request with the same
        // generic message to avoid revealing what happened. The grace window
        // bounds how long a replayed old token is tolerated (Jwt:RefreshReplayGraceSeconds).
        if (stored.Revoked)
        {
            var successor = await db.RefreshTokens
                .Where(rt => rt.UserId == stored.UserId && !rt.Revoked && rt.ExpiresAt > timeProvider.UtcNow)
                .OrderByDescending(rt => rt.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var reuseIsRecent = successor is not null
                && timeProvider.UtcNow - successor.CreatedAt <= TimeSpan.FromSeconds(jwtOptions.Value.RefreshReplayGraceSeconds);
            if (!reuseIsRecent)
            {
                await RevokeAllUserTokensAsync(stored.UserId, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return new RefreshTokenResult(false, "Invalid or expired refresh token.", null, null, null, null);
        }

        var user = await db.Users.FindAsync([stored.UserId], cancellationToken);
        if (user is null)
        {
            return new RefreshTokenResult(false, "User no longer exists.", null, null, null, null);
        }

        // A locked-out account must not be able to refresh its session. Reject
        // rotation and revoke the token so a locked user cannot retry with it.
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > timeProvider.UtcNow)
        {
            stored.Revoked = true;
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new RefreshTokenResult(false, "Account is temporarily locked.", null, null, null, null);
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
            communeId: user.CommuneId, securityStamp: user.SecurityStamp,
            role: user.Role, dairaId: user.DairaId, wilayaId: user.WilayaId);

        return new RefreshTokenResult(true, null, user.Username, newRaw, newAccessToken, refreshExpiry);
    }

    public async Task<RefreshTokenResult> MintAccessTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rawRefreshToken))
        {
            return new RefreshTokenResult(false, "No refresh token.", null, null, null, null);
        }

        var hash = HashRefreshToken(rawRefreshToken);

        // Read-only: no rotation, no row lock. Concurrent page loads may all
        // validate the same token without one revoking it for the others.
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash && !rt.Revoked && rt.ExpiresAt > timeProvider.UtcNow, cancellationToken);

        if (stored is null)
        {
            return new RefreshTokenResult(false, "Invalid or expired refresh token.", null, null, null, null);
        }

        var user = await db.Users.FindAsync([stored.UserId], cancellationToken);
        if (user is null)
        {
            return new RefreshTokenResult(false, "User no longer exists.", null, null, null, null);
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > timeProvider.UtcNow)
        {
            return new RefreshTokenResult(false, "Account is temporarily locked.", null, null, null, null);
        }

        var newAccessToken = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email,
            communeId: user.CommuneId, securityStamp: user.SecurityStamp,
            role: user.Role, dairaId: user.DairaId, wilayaId: user.WilayaId);

        return new RefreshTokenResult(true, null, user.Username, null, newAccessToken, stored.ExpiresAt);
    }

    public virtual async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default) => await db.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.Revoked)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(rt => rt.Revoked, true), cancellationToken);

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

    public async Task RecordFailedLoginAsync(User user, int maxFailedAttempts, int lockoutMinutes, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        // While a lockout is active, ignore further failures: counting them
        // would let anyone who knows a username extend the lockout indefinitely
        // with a steady trickle of bad passwords.
        var now = utcNow.UtcDateTime;
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > now)
        {
            return;
        }

        // A lockout that has already expired starts a fresh counting cycle,
        // so re-locking requires another full sequence of failed attempts.
        if (await RecordFailedLoginInStoreAsync(user, maxFailedAttempts, lockoutMinutes, now, cancellationToken))
        {
            // Rotate the security stamp so any access tokens issued before the
            // lockout are rejected immediately (see AuthenticationExtensions
            // OnTokenValidated) instead of remaining valid until expiry.
            stampCache.EvictStamp(user.Id);
        }
    }

    /// <summary>
    /// Applies a failed login atomically in the database and reports whether a
    /// fresh lockout was just applied.
    ///
    /// The failed-login counter is incremented with a row-level atomic UPDATE
    /// (<c>failed_login_attempts = failed_login_attempts + 1</c>) instead of a
    /// read-modify-write on a tracked entity, so two concurrent bad-password
    /// attempts cannot both persist the same value and lose one increment
    /// (which would delay or dodge the lockout threshold). Virtual so unit
    /// tests can override it with a tracked-entity equivalent — the InMemory
    /// provider cannot execute <c>ExecuteUpdateAsync</c>.
    /// </summary>
    protected virtual async Task<bool> RecordFailedLoginInStoreAsync(
        User user, int maxFailedAttempts, int lockoutMinutes, DateTime now, CancellationToken cancellationToken)
    {
        // Clear an expired lockout so the fresh cycle starts counting from zero.
        await db.Users
            .Where(u => u.Id == user.Id && u.LockedUntil != null && u.LockedUntil <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.LockedUntil, u => (DateTime?)null)
                .SetProperty(u => u.FailedLoginAttempts, 0), cancellationToken);

        // Atomic increment. Skipping rows that are currently locked keeps an
        // active lockout immune to extension by further bad passwords.
        await db.Users
            .Where(u => u.Id == user.Id && u.LockedUntil == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedLoginAttempts, u => u.FailedLoginAttempts + 1), cancellationToken);

        var attempts = await db.Users.AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => u.FailedLoginAttempts)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempts < maxFailedAttempts)
        {
            return false;
        }

        // Threshold reached: lock the account, reset the counter so re-locking
        // after expiry needs another full sequence, and rotate the security stamp.
        var newStamp = User.GenerateSecurityStamp();
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.LockedUntil, now.AddMinutes(lockoutMinutes))
                .SetProperty(u => u.FailedLoginAttempts, 0)
                .SetProperty(u => u.SecurityStamp, newStamp), cancellationToken);
        return true;
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

#pragma warning disable S2077 // Table name is allowlist-validated; parameters used for token_hash and cutoff
        var cutoff = timeProvider.UtcNow;
        // Note: intentionally does NOT filter revoked — the caller distinguishes
        // a valid token from a replay of a rotated one to revoke the family.
        return db.RefreshTokens
            .FromSqlRaw(
                $"SELECT * FROM {expectedTable} WHERE token_hash = {{0}} AND expires_at > {{1}} FOR UPDATE SKIP LOCKED",
                hash, cutoff)
            .FirstOrDefaultAsync(ct);
#pragma warning restore S2077
    }
}
