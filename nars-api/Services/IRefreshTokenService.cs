using NarsApi.DTOs;

namespace NarsApi.Services;

public interface IRefreshTokenService
{
    /// <summary>Validates a refresh token, revokes it, and issues a new one (rotation).</summary>
    Task<RefreshTokenResult> RotateRefreshTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default);
}
