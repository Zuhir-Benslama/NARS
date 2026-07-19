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
    IRefreshTokenService refreshService,
    IJwtService jwt,
    IOptions<AccountLockoutOptions> lockoutOptions,
    IOptions<AdminSignupOptions> adminSignupOptions,
    ILogger<AuthController> logger,
    IDateTimeProvider timeProvider,
    IUserAuthorizationService authorizationService,
    IUserCreationService userCreationService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    // Stable dummy hash so BCrypt always does the full work, even for unknown users.
    // Prevents username enumeration via response-time side-channel.
    // NOTE: Only applies to signin and authorized-signup — refresh uses token hash, not BCrypt.
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
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest body, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = body.Username.ToLowerInvariant();
        var user = await refreshService.FindUserByUsernameAsync(normalizedUsername, cancellationToken);

        var hashToCheck = user?.PasswordHash ?? DummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(body.Password, hashToCheck);

        var isLocked = user?.LockedUntil.HasValue == true && user.LockedUntil.Value > timeProvider.UtcNow;

        if (isLocked || !passwordValid || user is null)
        {
            if (!passwordValid && user is not null)
            {
                await refreshService.RecordFailedLoginAsync(user, MaxFailedAttempts, LockoutMinutes, timeProvider.UtcNow, cancellationToken);
            }

            return Problem(detail: "Invalid username or password", statusCode: 401);
        }

        await refreshService.ResetFailedAttemptsIfNeededAsync(user, cancellationToken);

        return await BuildSignInResponseAsync(user, cancellationToken);
    }

    // ── POST /api/logout ──────────────────────────────────────

    /// <summary>Revokes all refresh tokens and clears auth cookies.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        var userIdStr = User.FindFirstValue(ClaimNames.UserId);
        if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out Guid userId))
        {
            await refreshService.RevokeAllUserTokensAsync(userId, cancellationToken);
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
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken = default)
    {
        var result = await refreshService.RotateRefreshTokenAsync(Request.Cookies["refresh_token"], cancellationToken);
        if (!result.Success)
        {
            return Problem(detail: result.Detail, statusCode: 401);
        }

        if (result.RefreshExpiry is null || result.NewAccessToken is null || result.NewRawToken is null)
        {
            return Problem(detail: "Token refresh succeeded but token data is missing.", statusCode: 500);
        }

        var cookieMaxAge = result.RefreshExpiry.Value - timeProvider.UtcNow;
        Response.Cookies.Append("access_token", result.NewAccessToken, MakeCookieOptions(jwt.AccessTokenExpiresIn));
        Response.Cookies.Append("refresh_token", result.NewRawToken, MakeCookieOptions(cookieMaxAge));

        return Ok(new RefreshResponse(Success: true, TokenType: "bearer"));
    }

    // ── GET /api/current_user ─────────────────────────────────

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
        var user = await refreshService.FindUserByIdAsync(userId, cancellationToken);
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

    private async Task<IActionResult> BuildSignInResponseAsync(Models.User user, CancellationToken ct)
    {
        var token = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email,
            communeId: user.CommuneId, role: user.Role, dairaId: user.DairaId, wilayaId: user.WilayaId);

        var (refreshRaw, _, refreshExpiry) = await refreshService.IssueRefreshTokenAsync(user.Id, ct);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Setting auth cookies, Secure={IsHttps}", Request.IsHttps);
        }

        var refreshMaxAge = refreshExpiry - timeProvider.UtcNow;
        Response.Cookies.Append("access_token", token, MakeCookieOptions(jwt.AccessTokenExpiresIn));
        Response.Cookies.Append("refresh_token", refreshRaw, MakeCookieOptions(refreshMaxAge));

        var loc = await LoadCommuneWithDairaAsync(user.CommuneId ?? 0, ct);

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

    // ── Private helpers ───────────────────────────────────────

    private sealed record LocationChain(Commune? Commune, Daira? Daira, Wilaya? Wilaya);
    private sealed record CommuneWithDaira(Commune? Commune, Daira? Daira);

    private async Task<T> WithLocationDb<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var db = refreshService.CreateDbContext();
        return await query(db);
    }

    private Task<LocationChain> LoadLocationChainAsync(int communeId, CancellationToken cancellationToken = default)
        => WithLocationDb(db => (
            from c in db.Communes
            where c.CommuneId == communeId
            join d in db.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            join w in db.Wilayas on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync(cancellationToken).ContinueWith(t =>
            t.Result is null
                ? new LocationChain(null, null, null)
                : new LocationChain(t.Result.Commune, t.Result.Daira, t.Result.Wilaya),
            cancellationToken));

    private Task<CommuneWithDaira> LoadCommuneWithDairaAsync(int communeId, CancellationToken cancellationToken = default)
        => WithLocationDb(db => (
            from c in db.Communes
            where c.CommuneId == communeId
            join d in db.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d }
        ).FirstOrDefaultAsync(cancellationToken).ContinueWith(t =>
            t.Result is null
                ? new CommuneWithDaira(null, null)
                : new CommuneWithDaira(t.Result.Commune, t.Result.Daira),
            cancellationToken));

    private Task<LocationChain> LoadDairaWithWilayaAsync(int dairaId, CancellationToken cancellationToken = default)
        => WithLocationDb(db => (
            from d in db.Dairas
            where d.DairaId == dairaId
            join w in db.Wilayas on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Daira = d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync(cancellationToken).ContinueWith(t =>
            t.Result is null
                ? new LocationChain(null, null, null)
                : new LocationChain(null, t.Result.Daira, t.Result.Wilaya),
            cancellationToken));

    private async Task<LocationChain> LoadWilayaOnlyAsync(int? wilayaId, CancellationToken cancellationToken = default)
    {
        if (!wilayaId.HasValue)
        {
            return new LocationChain(null, null, null);
        }

        await using var db = refreshService.CreateDbContext();
        var wilaya = await db.Wilayas.FindAsync([wilayaId.Value], cancellationToken);
        return new LocationChain(null, null, wilaya);
    }
}
