using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Services;

public interface IRefreshTokenService
{
    /// <summary>Validates a refresh token, revokes it, and issues a new one (rotation).</summary>
    Task<RefreshTokenResult> RotateRefreshTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default);
    /// <summary>Revokes all refresh tokens for the specified user.</summary>
    Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default);
    /// <summary>Creates and persists a new refresh token for the user, returning (rawToken, hash, expiresAt).</summary>
    Task<(string RawToken, string Hash, DateTime ExpiresAt)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);
    /// <summary>Records a failed login attempt and applies lockout if threshold reached.</summary>
    Task RecordFailedLoginAsync(User user, int maxFailedAttempts, int lockoutMinutes, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    /// <summary>Resets failed login attempts and clears lockout.</summary>
    Task ResetFailedAttemptsIfNeededAsync(User user, CancellationToken cancellationToken = default);
}
