using NarsApi.DTOs;

namespace NarsApi.Services;

public interface IRefreshTokenService
{
    /// <summary>Validates a refresh token, revokes it, and issues a new one (rotation).</summary>
    Task<RefreshTokenResult> RotateRefreshTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default);
    /// <summary>
    /// Validates a refresh token without rotating it and mints a fresh access token.
    /// Used for read-only page loads so concurrent tabs never consume the one-time-use
    /// refresh token and log each other out.
    /// </summary>
    Task<RefreshTokenResult> MintAccessTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default);
    /// <summary>Revokes all refresh tokens for the specified user.</summary>
    Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default);
    /// <summary>Creates and persists a new refresh token for the user, returning (rawToken, hash, expiresAt).</summary>
    Task<(string RawToken, string Hash, DateTime ExpiresAt)> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);
}
