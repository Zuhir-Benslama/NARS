using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NarsApi.Infrastructure;

namespace NarsApi.Controllers;

/// <summary>
/// Base class for all controllers that require an authenticated user.
/// Applying [Authorize] here routes unauthenticated requests through the
/// JWT bearer pipeline (OnMessageReceived -> 401) instead of duplicating
/// manual cookie->token->principal validation in every controller.
/// </summary>
[Authorize]
public abstract class NarsControllerBase(
    IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>The authenticated user's database ID (UUID v7), or null if absent.</summary>
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimNames.UserId), out Guid id)
            ? id
            : null;

    /// <summary>The authenticated user's database ID, guaranteed non-null.</summary>
    protected Guid RequiredCurrentUserId =>
        CurrentUserId ?? throw new UnauthorizedAccessException("user_id claim missing — endpoint requires authentication.");

    /// <summary>The authenticated user's username.</summary>
    protected string CurrentUsername =>
        User.FindFirstValue(ClaimNames.Username) ?? string.Empty;

    /// <summary>The role of the authenticated user (empty if absent).</summary>
    protected string CurrentUserRole =>
        User.FindFirstValue(ClaimNames.Role)
        ?? User.FindFirstValue(ClaimTypes.Role)
        ?? string.Empty;

    /// <summary>The commune ID for commune_user accounts, or null for admin accounts.</summary>
    protected int? CurrentCommuneId =>
        int.TryParse(User.FindFirstValue(ClaimNames.CommuneId), out var id) && id > 0
            ? id
            : null;

    /// <summary>The commune ID, guaranteed non-null.</summary>
    protected int RequiredCommuneId =>
        CurrentCommuneId
            ?? throw new UnauthorizedAccessException("commune_id claim missing — endpoint requires commune_user role.");

    /// <summary>The daira ID for daira_admin accounts, or null for other roles.</summary>
    protected int? CurrentDairaId =>
        int.TryParse(User.FindFirstValue(ClaimNames.DairaId), out var id) && id > 0
            ? id
            : null;

    /// <summary>The wilaya ID for wilaya_admin accounts, or null for other roles.</summary>
    protected int? CurrentWilayaId =>
        int.TryParse(User.FindFirstValue(ClaimNames.WilayaId), out var id) && id > 0
            ? id
            : null;

    /// <summary>Creates a consistent CookieOptions with secure defaults for auth cookies.</summary>
#pragma warning disable S2092 // Intentional: Secure is conditional on IsProduction() || IsHttps
    protected CookieOptions MakeCookieOptions(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = environment.IsProduction() || Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = maxAge,
        Path = "/",
        IsEssential = true,
    };
#pragma warning restore S2092

    /// <summary>Writes the access + refresh auth cookies with consistent secure options.</summary>
    protected void AppendAuthCookies(string accessToken, string refreshToken, TimeSpan accessMaxAge, TimeSpan refreshMaxAge)
    {
        Response.Cookies.Append("access_token", accessToken, MakeCookieOptions(accessMaxAge));
        Response.Cookies.Append("refresh_token", refreshToken, MakeCookieOptions(refreshMaxAge));
    }
}
