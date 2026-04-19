using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Tags("Auth")]
public class AuthController(
    AppDbContext db,
    JwtService jwt,
    IConfiguration config,
    ILogger<AuthController> logger
) : ControllerBase
{
    // ── POST /api/signup ──────────────────────────────────────

    [HttpPost("/api/signup")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequest body)
    {
        // Password strength: minimum 8 characters with complexity requirements
        var pwdErr = PasswordValidator.Validate(body.Password);
        if (pwdErr is not null)
            return BadRequest(new { detail = pwdErr });

        // Validate that the commune actually exists
        var communeExists = await db.Communes.AnyAsync(c => c.CommuneId == body.CommuneId);
        if (!communeExists)
            return BadRequest(new { detail = "Invalid commune. Please select a valid commune." });

        var existing = await db.Users.FirstOrDefaultAsync(u =>
            u.Username == body.Username || u.Email == body.Email);

        if (existing is not null)
        {
            var field = existing.Username == body.Username ? "Username" : "Email";
            return Conflict(new { detail = $"{field} already exists" });
        }

        var user = new User
        {
            Name = body.Name,
            Email = body.Email,
            Phone = body.Phone,
            Username = body.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password),
            CommuneId = body.CommuneId,
            FailedLoginAttempts = 0,
            LockedUntil = null,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return StatusCode(201, new { success = true, message = "User registered successfully", user_id = user.Id });
    }

    // ── POST /api/signin ──────────────────────────────────────

    [HttpPost("/api/signin")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest body)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == body.Username);
        if (user is null)
            return Unauthorized(new { detail = "Invalid username or password" });

        // Check if account is locked
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            var remaining = (int)(user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes + 1;
            return Unauthorized(new { detail = $"Account locked due to too many failed attempts. Try again in {remaining} minute{(remaining == 1 ? "" : "s")}." });
        }

        if (!BCrypt.Net.BCrypt.Verify(body.Password, user.PasswordHash))
        {
            await RecordFailedLogin(user);
            return Unauthorized(new { detail = "Invalid username or password" });
        }

        // Successful login — reset failed attempts
        if (user.FailedLoginAttempts > 0 || user.LockedUntil.HasValue)
        {
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync();
        }

        var token = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email, user.CommuneId);

        // Issue a refresh token for silent re-authentication before the access token expires.
        var (refreshRaw, refreshHash) = JwtService.CreateRefreshToken();
        var refreshExpiry = DateTime.UtcNow.AddDays(
            ParseIntConfig(config["Jwt:RefreshExpiresInDays"], 30));

        logger.LogDebug("Setting auth cookies, Secure={IsHttps}", Request.IsHttps);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiry,
        });
        await db.SaveChangesAsync();

        // fix #5: Secure = true only when the request itself is HTTPS.
        // Using Request.IsHttps instead of IsDevelopment() avoids the
        // "cookie silently dropped on HTTP because Secure is true" trap
        // when ASPNETCORE_ENVIRONMENT is unset but the server is running
        // behind a local HTTP dev server.
        var refreshMaxAge = refreshExpiry - DateTime.UtcNow;

        Response.Cookies.Append("access_token", token, MakeCookieOptions(TimeSpan.FromHours(24)));
        Response.Cookies.Append("refresh_token", refreshRaw, MakeCookieOptions(refreshMaxAge));

        // fix #7: single joined query instead of 3 sequential round-trips.
        // fix #11: wilaya is not part of the SignIn response — not loaded here.
        var loc = await LoadCommuneWithDairaAsync(user.CommuneId);

        return Ok(new
        {
            success = true,
            token = token,
            token_type = "bearer",
            user = new
            {
                id = user.Id.ToString(),
                username = user.Username,
                name = user.Name,
                email = user.Email,
                commune = new
                {
                    id = user.CommuneId,
                    name_fr = loc.Commune?.CommuneFr,
                    name_ar = loc.Commune?.CommuneAr,
                    latitude = loc.Commune?.CommuneLatitude,
                    longitude = loc.Commune?.CommuneLongitude,
                },
            },
        });
    }

    // ── POST /api/logout ──────────────────────────────────────

    [HttpPost("/api/logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        // Extract user_id from the authenticated token claims
        var userIdStr = User.FindFirstValue("user_id");
        if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out Guid userId))
        {
            // Revoke all refresh tokens for the current user
            await db.RefreshTokens
                .Where(rt => rt.UserId == userId && !rt.Revoked)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(rt => rt.Revoked, true));
        }

        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete("refresh_token");
        return Ok(new { success = true, message = "Logged out successfully" });
    }

    // ── POST /api/refresh — issue a new access token using a valid refresh token
    // Rate-limited to prevent refresh token brute-force attacks.
    [HttpPost("/api/refresh")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { detail = "No refresh token." });

        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(refreshToken)));

        // Wrap the entire read-check-rotate in a single transaction with a
        // row-level lock (FOR UPDATE SKIP LOCKED) to prevent concurrent
        // requests from both reading the same token as valid.
        await using var tx = await db.Database.BeginTransactionAsync();

        var stored = await db.RefreshTokens
            .FromSqlRaw(
                "SELECT * FROM refresh_tokens WHERE token_hash = {0} AND revoked = false AND expires_at > NOW() FOR UPDATE SKIP LOCKED",
                hash)
            .FirstOrDefaultAsync();

        if (stored is null)
        {
            await tx.RollbackAsync();
            return Unauthorized(new { detail = "Invalid or expired refresh token." });
        }

        // Load the user to create a fresh access token
        var user = await db.Users.FindAsync(stored.UserId);
        if (user is null)
        {
            await tx.RollbackAsync();
            return Unauthorized(new { detail = "User no longer exists." });
        }

        // Rotate: revoke old refresh token, issue a new one
        stored.Revoked = true;
        var (newRaw, newHash) = JwtService.CreateRefreshToken();
        var refreshExpiry = DateTime.UtcNow.AddDays(
            ParseIntConfig(config["Jwt:RefreshExpiresInDays"], 30));

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newHash,
            ExpiresAt = refreshExpiry,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var newToken = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email, user.CommuneId);

        var cookieMaxAge = refreshExpiry - DateTime.UtcNow;

        Response.Cookies.Append("access_token", newToken, MakeCookieOptions(TimeSpan.FromHours(24)));
        Response.Cookies.Append("refresh_token", newRaw, MakeCookieOptions(cookieMaxAge));

        return Ok(new { success = true, token_type = "bearer" });
    }

    // ── GET /api/current_user ─────────────────────────────────
    // fix #1: [Authorize] + User.FindFirst(...) replaces manual GetPrincipalFromCookie(),
    // routing unauthenticated requests through the standard JWT bearer pipeline → 401.

    [HttpGet("/api/current_user")]
    [Authorize]
    public async Task<IActionResult> CurrentUser()
    {
        var userIdStr = User.FindFirstValue("user_id");
        if (!Guid.TryParse(userIdStr, out Guid userId))
        {
            return Unauthorized(new { detail = "Malformed token claims." });
        }

        // Query the database for fresh user data instead of relying on
        // potentially stale JWT claims (user profile may have changed).
        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return Unauthorized(new { detail = "User no longer exists." });

        if (!int.TryParse(User.FindFirstValue("commune_id"), out int communeId))
            communeId = user.CommuneId;

        // fix #7: single joined query for commune → daira → wilaya.
        var loc = await LoadLocationChainAsync(communeId);

        return Ok(new
        {
            id = user.Id.ToString(),
            username = user.Username,
            name = user.Name,
            email = user.Email,
            wilaya = new
            {
                id = loc.Wilaya?.WilayaId,
                name_fr = loc.Wilaya?.WilayaFr,
                name_ar = loc.Wilaya?.WilayaAr,
                latitude = loc.Wilaya?.WilayaLatitude,
                longitude = loc.Wilaya?.WilayaLongitude,
            },
            daira = new
            {
                id = loc.Daira?.DairaId,
                name_fr = loc.Daira?.DairaFr,
                name_ar = loc.Daira?.DairaAr,
                latitude = loc.Daira?.DairaLatitude,
                longitude = loc.Daira?.DairaLongitude,
            },
            commune = new
            {
                id = communeId,
                name_fr = loc.Commune?.CommuneFr,
                name_ar = loc.Commune?.CommuneAr,
                latitude = loc.Commune?.CommuneLatitude,
                longitude = loc.Commune?.CommuneLongitude,
            },
        });
    }

    // ── Account lockout helpers ───────────────────────────────

    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 30;

    private static int ParseIntConfig(string? value, int defaultValue)
    {
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private async Task RecordFailedLogin(User user)
    {
        user.FailedLoginAttempts = (user.FailedLoginAttempts ?? 0) + 1;
        if (user.FailedLoginAttempts >= MaxFailedAttempts)
            user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);

        await db.SaveChangesAsync();
    }

    // ── Private helpers ───────────────────────────────────────

    private record LocationChain(Commune? Commune, Daira? Daira, Wilaya? Wilaya);
    private record CommuneWithDaira(Commune? Commune, Daira? Daira);

    /// <summary>
    /// fix #7: Loads commune → daira → wilaya in one SQL JOIN.
    /// </summary>
    private async Task<LocationChain> LoadLocationChainAsync(int communeId)
    {
        var row = await (
            from c in db.Communes
            where c.CommuneId == communeId
            join d in db.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            join w in db.Wilayas on d.WilayaId equals w.WilayaId into wj
            from w in wj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d, Wilaya = (Wilaya?)w }
        ).FirstOrDefaultAsync();

        return row is null
            ? new LocationChain(null, null, null)
            : new LocationChain(row.Commune, row.Daira, row.Wilaya);
    }

    /// <summary>
    /// fix #7 + fix #11: SignIn only needs commune + daira (wilaya absent from response).
    /// </summary>
    private async Task<CommuneWithDaira> LoadCommuneWithDairaAsync(int communeId)
    {
        var row = await (
            from c in db.Communes
            where c.CommuneId == communeId
            join d in db.Dairas on c.DairaId equals d.DairaId into dj
            from d in dj.DefaultIfEmpty()
            select new { Commune = c, Daira = (Daira?)d }
        ).FirstOrDefaultAsync();

        return row is null
            ? new CommuneWithDaira(null, null)
            : new CommuneWithDaira(row.Commune, row.Daira);
    }

    /// <summary>
    /// Creates a consistent CookieOptions with secure defaults for auth cookies.
    /// </summary>
    private CookieOptions MakeCookieOptions(TimeSpan maxAge)
    {
        var isHttps = Request.IsHttps;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = maxAge,
            Path = "/",
            IsEssential = true,
        };
    }
}
