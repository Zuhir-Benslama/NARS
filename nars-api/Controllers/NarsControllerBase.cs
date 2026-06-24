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
public abstract class NarsControllerBase : ControllerBase
{
    /// <summary>
    /// The authenticated user's database ID (UUID v7), or null if the claim is absent.
    /// Returns null rather than throwing, matching the pattern of CommuneId/DairaId.
    /// Use <see cref="RequiredCurrentUserId"/> if a non-null value is guaranteed.
    /// </summary>
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimNames.UserId), out Guid id)
            ? id
            : null;

    /// <summary>
    /// The authenticated user's database ID, guaranteed non-null.
    /// Throws if the claim is absent — use only in endpoints protected by [Authorize].
    /// </summary>
    protected Guid RequiredCurrentUserId =>
        CurrentUserId ?? throw new InvalidOperationException("user_id claim missing — endpoint requires authentication.");

    /// <summary>The authenticated user's username.</summary>
    protected string CurrentUsername =>
        User.FindFirstValue(ClaimNames.Username) ?? string.Empty;

    /// <summary>
    /// The role of the authenticated user (e.g. "commune_user", "daira_admin").
    /// Defaults to "commune_user" if the claim is absent (backward-compatible with
    /// tokens issued before the roles migration).
    /// </summary>
    protected string CurrentUserRole =>
        User.FindFirstValue(ClaimNames.Role) ?? UserRoles.CommuneUser;

    /// <summary>
    /// The commune ID for commune_user accounts, or null for admin accounts.
    /// Returns null rather than throwing so admin controllers that don't need
    /// a commune_id don't need to catch exceptions.
    /// </summary>
    protected int? CurrentCommuneId =>
        int.TryParse(User.FindFirstValue(ClaimNames.CommuneId), out var id) && id > 0
            ? id
            : null;

    /// <summary>
    /// The commune ID, guaranteed non-null. Throws if the claim is absent —
    /// use only in endpoints restricted to commune_user accounts.
    /// </summary>
    protected int RequiredCommuneId =>
        CurrentCommuneId
            ?? throw new InvalidOperationException("commune_id claim missing — endpoint requires commune_user role.");

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

    /// <summary>
    /// Creates a consistent CookieOptions with secure defaults for auth cookies.
    /// </summary>
    protected CookieOptions MakeCookieOptions(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = maxAge,
        Path = "/",
        IsEssential = true,
    };


}
