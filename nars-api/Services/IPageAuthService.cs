using System.Security.Claims;

namespace NarsApi.Services;

/// <summary>
/// Handles page-level authentication for server-rendered HTML pages.
/// Validates access tokens from cookies/bearer headers and performs
/// silent refresh to keep page sessions alive.
/// </summary>
public interface IPageAuthService
{
    /// <summary>
    /// Attempts to authenticate the current request by checking for a valid
    /// access token (cookie or bearer header) or performing a silent refresh.
    /// Returns true if the request is authenticated.
    /// </summary>
    Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts a silent refresh: validates the refresh token cookie and
    /// mints a new access token without rotating the refresh token.
    /// Returns true on success.
    /// </summary>
    Task<bool> TryRefreshSessionAsync(CancellationToken cancellationToken = default);
}
