using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

/// <summary>
/// Handles page-level authentication for server-rendered HTML pages.
/// Validates access tokens from cookies/bearer headers and performs
/// silent refresh to keep page sessions alive without rotating the
/// one-time-use refresh token.
/// </summary>
public sealed class PageAuthService(
    IHttpContextAccessor httpContextAccessor,
    IJwtService jwt,
    IRefreshTokenService refreshService,
    ISecurityStampCache stampCache,
    AppDbContext db,
    IHostEnvironment env,
    ILogger<PageAuthService> logger) : IPageAuthService
{
    private HttpContext HttpContext => httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("No active HttpContext.");

    public async Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var principal = await ValidateAccessTokenFromCookieAsync(cancellationToken);
        principal ??= await ValidateAccessTokenFromBearerHeaderAsync(cancellationToken);

        if (principal is not null)
        {
            return true;
        }

        return await TryRefreshSessionAsync(cancellationToken);
    }

    public async Task<bool> TryRefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        var refreshToken = HttpContext.Request.Cookies[CookieNames.RefreshToken];
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogDebug("[Pages] refresh_token cookie NOT FOUND. Cannot silent refresh.");
            return false;
        }

        logger.LogDebug("[Pages] Found refresh_token. Attempting silent refresh...");

        try
        {
            // Read-only page loads mint an access token WITHOUT rotating the
            // one-time-use refresh token, so concurrent tabs (or double-fetch
            // from /) never revoke it for each other and bounce to /login.
            // MintAccessTokenAsync reads the user's CURRENT security stamp and
            // rejects locked accounts, so the minted token is always fresh.
            var result = await refreshService.MintAccessTokenAsync(refreshToken, cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("[Pages] Refresh failed: {Detail}", result.Detail);
                return false;
            }

            if (result.NewAccessToken is null)
            {
                logger.LogWarning("[Pages] Refresh succeeded but access token is missing.");
                return false;
            }

            logger.LogDebug("[Pages] Silent refresh SUCCESS. Issuing new access cookie for {Username}", result.Username);
            HttpContext.Response.Cookies.Append(
                CookieNames.AccessToken,
                result.NewAccessToken,
                MakeCookieOptions(jwt.AccessTokenExpiresIn));

            var principal = jwt.ValidateToken(result.NewAccessToken);

            return principal is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[Pages] Error during silent refresh");
            return false;
        }
    }

    private async Task<ClaimsPrincipal?> ValidateAccessTokenFromCookieAsync(CancellationToken ct)
    {
        var accessToken = HttpContext.Request.Cookies[CookieNames.AccessToken];
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        var principal = await ValidateSecurityStampAsync(jwt.ValidateToken(accessToken), ct);
        if (principal is not null)
        {
            logger.LogDebug("[Pages] access_token cookie is valid.");
        }
        else
        {
            logger.LogDebug("[Pages] access_token cookie is EXPIRED, INVALID, or STAMPED OUT.");
        }

        return principal;
    }

    private async Task<ClaimsPrincipal?> ValidateAccessTokenFromBearerHeaderAsync(CancellationToken ct)
    {
        var bearerHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(bearerHeader)
            || !bearerHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bearerToken = bearerHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(bearerToken))
        {
            return null;
        }

        var principal = await ValidateSecurityStampAsync(jwt.ValidateToken(bearerToken), ct);
        if (principal is not null)
        {
            logger.LogDebug("[Pages] Valid bearer token header found.");
        }
        else
        {
            logger.LogDebug("[Pages] Bearer token header is invalid, expired, or stamped out.");
        }

        // Never persist a header-supplied bearer token into the access_token
        // cookie: the client chose to send it via header (not as a cookie), and
        // writing it as a long-lived cookie would broaden the token's exposure.
        return principal;
    }

    /// <summary>
    /// Re-checks the token's embedded security stamp against the current one,
    /// mirroring the JwtBearer <c>OnTokenValidated</c> handler so page sessions
    /// revoke immediately on lockout/password change instead of staying valid
    /// until the token or cookie expires.
    /// </summary>
    private async Task<ClaimsPrincipal?> ValidateSecurityStampAsync(ClaimsPrincipal? principal, CancellationToken ct)
    {
        if (principal is null)
        {
            return null;
        }

        var userId = Guid.TryParse(principal.FindFirstValue(ClaimNames.UserId), out var id) ? id : (Guid?)null;
        var stamp = principal.FindFirstValue(ClaimNames.SecurityStamp);
        if (userId is null || string.IsNullOrEmpty(stamp))
        {
            return null;
        }

        var current = await stampCache.GetStampAsync(userId.Value, ct);
        if (current is null)
        {
            // Cache miss — query DB and populate cache for next request.
            current = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(ct);

            if (current is not null)
            {
                stampCache.SetStamp(userId.Value, current);
            }
        }

        return current == stamp ? principal : null;
    }

    private CookieOptions MakeCookieOptions(TimeSpan maxAge)
        => CookieHelper.MakeCookieOptions(maxAge, env.IsProduction() || HttpContext.Request.IsHttps);
}
