using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
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
    ILocationQueryService locationQuery,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
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
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var normalizedUsername = body.Username.ToLowerInvariant();
        var credentialResult = await authorizationService.VerifyCredentialsAsync(
            normalizedUsername, body.Password, MaxFailedAttempts, LockoutMinutes, cancellationToken);

        if (!credentialResult.IsSuccess)
        {
            // Deliberate single generic 401 for every failure (bad password AND
            // lockout) to keep the public sign-in endpoint enumeration-resistant.
            // Admin sign-up (AuthController.AdminSignup) reports 423 for a locked
            // admin because that path already proves ownership of an admin account.
            return Problem(detail: "Invalid username or password", statusCode: 401);
        }

        var user = credentialResult.User!;

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
        if (CurrentUserId is { } userId)
        {
            await refreshService.RevokeAllUserTokensAsync(userId, cancellationToken);
        }

        // Match the HttpOnly/Secure/SameSite/Path attributes used when writing the
        // cookies so the deletion applies to the same cookies regardless of how
        // cookie attribute matching is implemented by the client.
        Response.Cookies.Delete(CookieNames.AccessToken, MakeCookieOptions(TimeSpan.Zero));
        Response.Cookies.Delete(CookieNames.RefreshToken, MakeCookieOptions(TimeSpan.Zero));
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
        var result = await refreshService.RotateRefreshTokenAsync(Request.Cookies[CookieNames.RefreshToken], cancellationToken);
        if (!result.Success)
        {
            return Problem(detail: result.Detail, statusCode: 401);
        }

        if (result.RefreshExpiry is null || result.NewAccessToken is null || result.NewRawToken is null)
        {
            return Problem(detail: "Token refresh succeeded but token data is missing.", statusCode: 500);
        }

        var cookieMaxAge = result.RefreshExpiry.Value - timeProvider.UtcNow;
        AppendAuthCookies(result.NewAccessToken, result.NewRawToken, jwt.AccessTokenExpiresIn, cookieMaxAge);

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
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Problem(detail: "Malformed token claims.", statusCode: 401);
        }

        // Query the database for fresh user data instead of relying on
        // potentially stale JWT claims (user profile may have changed).
        var user = await authorizationService.FindUserByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return Problem(detail: "User no longer exists.", statusCode: 401);
        }

        // Prefer the freshly-loaded DB value over the (potentially stale) JWT
        // claim: if an admin moved the user to another commune right after
        // token issuance, the location chain must reflect the new scope.
        var communeId = user.CommuneId ?? 0;

        Models.Daira? daira = null;
        Models.Commune? commune = null;

        // Load location chain — single JOIN query for commune→daira→wilaya,
        // or single JOIN for daira→wilaya when no commune is assigned.
        Wilaya? wilaya;
        if (communeId > 0)
        {
            var chain = await locationQuery.GetLocationChainAsync(communeId, cancellationToken);
            commune = chain.Commune;
            daira = chain.Daira;
            wilaya = chain.Wilaya;
        }
        else if (user.DairaId.HasValue)
        {
            var dairaResult = await locationQuery.GetDairaWithWilayaAsync(user.DairaId.Value, cancellationToken);
            daira = dairaResult.Daira;
            wilaya = dairaResult.Wilaya;
        }
        else
        {
            wilaya = await locationQuery.GetWilayaAsync(user.WilayaId, cancellationToken);
        }

        return Ok(new UserInfoWithLocation(
            Id: user.Id.ToString(),
            Username: user.Username,
            Name: user.Name,
            Email: user.Email,
            Role: user.Role,
            Wilaya: wilaya is not null
                ? new CommuneInfo(wilaya.WilayaId, wilaya.WilayaFr, wilaya.WilayaAr, wilaya.WilayaLatitude, wilaya.WilayaLongitude)
                : null,
            Daira: daira is not null
                ? new CommuneInfo(daira.DairaId, daira.DairaFr, daira.DairaAr, daira.DairaLatitude, daira.DairaLongitude)
                : null,
            Commune: commune is not null
                ? new CommuneInfo(commune.CommuneId, commune.CommuneFr, commune.CommuneAr, commune.CommuneLatitude, commune.CommuneLongitude)
                : null
        ));
    }

    // ── Account lockout helpers ───────────────────────────────

    private int MaxFailedAttempts => lockoutOptions.Value.MaxFailedAttempts;
    private int LockoutMinutes => lockoutOptions.Value.LockoutMinutes;

    private async Task<IActionResult> BuildSignInResponseAsync(Models.User user, CancellationToken ct)
    {
        var token = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email,
            communeId: user.CommuneId, securityStamp: user.SecurityStamp,
            role: user.Role, dairaId: user.DairaId, wilayaId: user.WilayaId);

        var (refreshRaw, _, refreshExpiry) = await refreshService.IssueRefreshTokenAsync(user.Id, ct);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Setting auth cookies, Secure={IsHttps}", Request.IsHttps);
        }

        var refreshMaxAge = refreshExpiry - timeProvider.UtcNow;
        AppendAuthCookies(token, refreshRaw, jwt.AccessTokenExpiresIn, refreshMaxAge);

        CommuneInfo? commune = null;
        if (user.CommuneId is { } communeId)
        {
            var loc = await locationQuery.GetCommuneWithDairaAsync(communeId, ct);
            if (loc.Commune is not null)
            {
                commune = new CommuneInfo(
                    loc.Commune.CommuneId,
                    loc.Commune.CommuneFr,
                    loc.Commune.CommuneAr,
                    loc.Commune.CommuneLatitude,
                    loc.Commune.CommuneLongitude
                );
            }
        }

        return Ok(new SignInResponse(
            Success: true,
            TokenType: "bearer",
            User: new UserInfo(
                Id: user.Id.ToString(),
                Username: user.Username,
                Name: user.Name,
                Email: user.Email,
                Role: user.Role,
                Commune: commune
            )
        ));
    }
}
