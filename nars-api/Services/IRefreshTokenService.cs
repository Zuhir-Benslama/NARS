namespace NarsApi.Services;

public interface IRefreshTokenService
{
    Task<RefreshTokenResult> RotateRefreshTokenAsync(string? rawRefreshToken, CancellationToken cancellationToken = default);
}
