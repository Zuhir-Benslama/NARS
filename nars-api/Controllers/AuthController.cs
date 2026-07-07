using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Auth")]
public partial class AuthController(
    AppDbContext db,
    IJwtService jwt,
    IOptions<JwtOptions> jwtOptions,
    IOptions<AccountLockoutOptions> lockoutOptions,
    ILogger<AuthController> logger,
    IDateTimeProvider timeProvider,
    IUserAuthorizationService authorizationService
) : NarsControllerBase
{
    // Stable dummy hash so BCrypt always does the full work, even for unknown users.
    // Prevents username enumeration via response-time side-channel.
    private const string DummyHash = "$2a$11$BCfJgwy.hTY703/9RBjPo.8UjBrTHh/95zFznkYLiapLvWdf5ISbO";
    // ── POST /api/signup — DISABLED ────────────────────────────────────────
    // Self-registration is not allowed. All accounts must be created by an
    // admin of the appropriate level:
    //   commune_user  → created by the daira_admin of the user's daira
    //   daira_admin   → created by the wilaya_admin of the daira's wilaya
    //   wilaya_admin  → created by the national_admin
    //   national_admin → created directly in the database
    // Use POST /api/admin/authorized-signup from the login page instead.
    [HttpPost("signup")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult SignUp() =>
        Problem(
            detail: "Self-registration is disabled. " +
            "Contact your daira admin to create a commune user account, " +
            "or use POST /api/admin/authorized-signup for admin accounts.",
            statusCode: 410);

    // ── POST /api/signin ──────────────────────────────────────

    /// <summary>Authenticates a user and issues access + refresh token cookies.</summary>
    [HttpPost("signin")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == body.Username, cancellationToken);

        // Always run BCrypt.Verify (even for unknown/locked users) to prevent
        // username enumeration via response-time side-channel (~0 µs vs ~300 ms).
        var hashToCheck = user?.PasswordHash ?? DummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(body.Password, hashToCheck);

        // Lockout check is after BCrypt so the timing is indistinguishable
        // from a wrong-password response.
        if (user is not null && user.LockedUntil.HasValue && user.LockedUntil.Value > timeProvider.UtcNow)
        {
            return Problem(detail: "Invalid username or password", statusCode: 401);
        }

        if (!passwordValid)
        {
            if (user is not null)
            {
                await RecordFailedLogin(user, cancellationToken);
            }
            return Problem(detail: "Invalid username or password", statusCode: 401);
        }

        if (user is null)
        {
            return Problem(detail: "Invalid username or password", statusCode: 401);
        }

        // Successful login — reset failed attempts
        if (user.FailedLoginAttempts > 0 || user.LockedUntil.HasValue)
        {
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email,
            communeId: user.CommuneId, role: user.Role, dairaId: user.DairaId, wilayaId: user.WilayaId);

        // Issue a refresh token for silent re-authentication before the access token expires.
        var (refreshRaw, refreshHash) = jwt.CreateRefreshToken();
        var refreshExpiry = timeProvider.UtcNow.AddDays(jwtOptions.Value.RefreshExpiresInDays);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Setting auth cookies, Secure={IsHttps}", Request.IsHttps);
        }

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiry,
        });
        await db.SaveChangesAsync(cancellationToken);

        // fix #5: Secure = true only when the request itself is HTTPS.
        // Using Request.IsHttps instead of IsDevelopment() avoids the
        // "cookie silently dropped on HTTP because Secure is true" trap
        // when ASPNETCORE_ENVIRONMENT is unset but the server is running
        // behind a local HTTP dev server.
        var refreshMaxAge = refreshExpiry - timeProvider.UtcNow;

        Response.Cookies.Append("access_token", token, MakeCookieOptions(jwt.AccessTokenExpiresIn));
        Response.Cookies.Append("refresh_token", refreshRaw, MakeCookieOptions(refreshMaxAge));

        // fix #7: single joined query instead of 3 sequential round-trips.
        // fix #11: wilaya is not part of the SignIn response — not loaded here.
        var loc = await LoadCommuneWithDairaAsync(user.CommuneId ?? 0, cancellationToken);

        return Ok(new SignInResponse(
            Success: true,
            Token: token,
            TokenType: "bearer",
            User: new UserInfo(
                Id: user.Id.ToString(),
                Username: user.Username,
                Name: user.Name,
                Email: user.Email,
                Role: user.Role,
                Commune: new CommuneInfo(
                    Id: user.CommuneId,
                    NameFr: loc.Commune?.CommuneFr,
                    NameAr: loc.Commune?.CommuneAr,
                    Latitude: loc.Commune?.CommuneLatitude,
                    Longitude: loc.Commune?.CommuneLongitude
                )
            )
        ));
    }

    // ── POST /api/logout ──────────────────────────────────────

    /// <summary>Revokes all refresh tokens and clears auth cookies.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        // Extract user_id from the authenticated token claims
        var userIdStr = User.FindFirstValue(ClaimNames.UserId);
        if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out Guid userId))
        {
            // Revoke all refresh tokens for the current user
            await db.RefreshTokens
                .Where(rt => rt.UserId == userId && !rt.Revoked)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(rt => rt.Revoked, true), cancellationToken);
        }

        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete("refresh_token");
        return Ok(ApiResponse.Ok("Logged out successfully"));
    }

    // ── POST /api/refresh — issue a new access token using a valid refresh token
    // Rate-limited to prevent refresh token brute-force attacks.
    /// <summary>Issues a new access token using a valid refresh token cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromServices] IRefreshTokenService refreshService, CancellationToken cancellationToken = default)
    {
        var result = await refreshService.RotateRefreshTokenAsync(Request.Cookies["refresh_token"], cancellationToken);
        if (!result.Success)
        {
            return Problem(detail: result.Detail, statusCode: 401);
        }

        var cookieMaxAge = result.RefreshExpiry!.Value - timeProvider.UtcNow;
        Response.Cookies.Append("access_token", result.NewAccessToken!, MakeCookieOptions(jwt.AccessTokenExpiresIn));
        Response.Cookies.Append("refresh_token", result.NewRawToken!, MakeCookieOptions(cookieMaxAge));

        return Ok(new RefreshResponse(Success: true, TokenType: "bearer"));
    }

    // ── GET /api/current_user ─────────────────────────────────
    // fix #1: [Authorize] + User.FindFirst(...) replaces manual GetPrincipalFromCookie(),
    // routing unauthenticated requests through the standard JWT bearer pipeline → 401.

    /// <summary>Returns the authenticated user's profile with location chain.</summary>
    [HttpGet("current_user")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CurrentUser(CancellationToken cancellationToken = default)
    {
        var userIdStr = User.FindFirstValue(ClaimNames.UserId);
        if (!Guid.TryParse(userIdStr, out Guid userId))
        {
            return Problem(detail: "Malformed token claims.", statusCode: 401);
        }

        // Query the database for fresh user data instead of relying on
        // potentially stale JWT claims (user profile may have changed).
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return Problem(detail: "User no longer exists.", statusCode: 401);
        }

        if (!int.TryParse(User.FindFirstValue(ClaimNames.CommuneId), out var communeId))
        {
            communeId = user.CommuneId ?? 0;
        }

        // Load location chain — single JOIN query for commune→daira→wilaya,
        // or single JOIN for daira→wilaya when no commune is assigned.
        LocationChain loc;
        if (communeId > 0)
        {
            loc = await LoadLocationChainAsync(communeId, cancellationToken);
        }
        else if (user.DairaId.HasValue)
        {
            loc = await LoadDairaWithWilayaAsync(user.DairaId.Value, cancellationToken);
        }
        else
        {
            loc = await LoadWilayaOnlyAsync(user.WilayaId, cancellationToken);
        }

        return Ok(new UserInfoWithLocation(
            Id: user.Id.ToString(),
            Username: user.Username,
            Name: user.Name,
            Email: user.Email,
            Role: user.Role,
            Wilaya: loc.Wilaya is not null
                ? new CommuneInfo(loc.Wilaya.WilayaId, loc.Wilaya.WilayaFr, loc.Wilaya.WilayaAr, loc.Wilaya.WilayaLatitude, loc.Wilaya.WilayaLongitude)
                : null,
            Daira: loc.Daira is not null
                ? new CommuneInfo(loc.Daira.DairaId, loc.Daira.DairaFr, loc.Daira.DairaAr, loc.Daira.DairaLatitude, loc.Daira.DairaLongitude)
                : null,
            Commune: communeId > 0
                ? new CommuneInfo(communeId, loc.Commune?.CommuneFr, loc.Commune?.CommuneAr, loc.Commune?.CommuneLatitude, loc.Commune?.CommuneLongitude)
                : null
        ));
    }

    // ── Account lockout helpers ───────────────────────────────

    private int MaxFailedAttempts => lockoutOptions.Value.MaxFailedAttempts;
    private int LockoutMinutes => lockoutOptions.Value.LockoutMinutes;

    private async Task RecordFailedLogin(User user, CancellationToken cancellationToken = default)
    {
        user.FailedLoginAttempts = (user.FailedLoginAttempts ?? 0) + 1;
        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            user.LockedUntil = timeProvider.UtcNow.AddMinutes(LockoutMinutes);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────

    private sealed record LocationChain(Commune? Commune, Daira? Daira, Wilaya? Wilaya);
    private sealed record CommuneWithDaira(Commune? Commune, Daira? Daira);

    /// <summary>
    /// fix #7: Loads commune → daira → wilaya in one SQL JOIN.
    /// </summary>
    private async Task<LocationChain> LoadLocationChainAsync(int communeId, CancellationToken cancellationToken = default)
    {
        var row = await (
            from c in db.Communes
            where c.CommuneId == communeId
            join d in db.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            join w in db.Wilayas on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? new LocationChain(null, null, null)
            : new LocationChain(row.Commune, row.Daira, row.Wilaya);
    }

    /// <summary>
    /// fix #7 + fix #11: SignIn only needs commune + daira (wilaya absent from response).
    /// </summary>
    private async Task<CommuneWithDaira> LoadCommuneWithDairaAsync(int communeId, CancellationToken cancellationToken = default)
    {
        var row = await (
            from c in db.Communes
            where c.CommuneId == communeId
            join d in db.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d }
        ).FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? new CommuneWithDaira(null, null)
            : new CommuneWithDaira(row.Commune, row.Daira);
    }

    /// <summary>
    /// Loads daira → wilaya in one JOIN (avoids N+1 from sequential FindAsync calls).
    /// </summary>
    private async Task<LocationChain> LoadDairaWithWilayaAsync(int dairaId, CancellationToken cancellationToken = default)
    {
        var row = await (
            from d in db.Dairas
            where d.DairaId == dairaId
            join w in db.Wilayas on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Daira = d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? new LocationChain(null, null, null)
            : new LocationChain(null, row.Daira, row.Wilaya);
    }

    /// <summary>
    /// Single-query wilaya lookup for national-level admins with no commune/daira.
    /// </summary>
    private async Task<LocationChain> LoadWilayaOnlyAsync(int? wilayaId, CancellationToken cancellationToken = default)
    {
        if (!wilayaId.HasValue)
        {
            return new LocationChain(null, null, null);
        }

        var wilaya = await db.Wilayas.FindAsync([wilayaId.Value], cancellationToken);
        return new LocationChain(null, null, wilaya);
    }
}
