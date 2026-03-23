using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Tags("Auth")]
public class AuthController(
    AppDbContext db,
    JwtService jwt,
    IWebHostEnvironment env   // fix #5: needed to set Secure flag conditionally
) : ControllerBase
{
    // ── POST /api/signup ──────────────────────────────────────

    [HttpPost("/api/signup")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequest body)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u =>
            u.Username == body.Username || u.Email == body.Email);

        if (existing is not null)
        {
            var field = existing.Username == body.Username ? "Username" : "Email";
            return Conflict(new { detail = $"{field} already exists" });
        }

        var user = new User
        {
            Name         = body.Name,
            Email        = body.Email,
            Phone        = body.Phone,
            Username     = body.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password),
            CommuneId    = body.CommuneId,
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
        if (user is null || !BCrypt.Net.BCrypt.Verify(body.Password, user.PasswordHash))
            return Unauthorized(new { detail = "Invalid username or password" });

        var token = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email, user.CommuneId);

        // fix #5: Secure = true in production so the cookie is never sent over plain HTTP.
        Response.Cookies.Append("access_token", token, new CookieOptions
        {
            HttpOnly    = true,
            Secure      = !env.IsDevelopment(),
            MaxAge      = TimeSpan.FromHours(24),
            SameSite    = SameSiteMode.Lax,
            Path        = "/",
            IsEssential = true,
        });

        // fix #7: single joined query instead of 3 sequential round-trips.
        // fix #11: wilaya is not part of the SignIn response — not loaded here.
        var loc = await LoadCommuneWithDairaAsync(user.CommuneId);

        return Ok(new
        {
            success    = true,
            token_type = "bearer",
            user = new
            {
                id       = user.Id,
                username = user.Username,
                name     = user.Name,
                email    = user.Email,
                commune  = new
                {
                    id        = user.CommuneId,
                    name_fr   = loc.Commune?.CommuneFr,
                    name_ar   = loc.Commune?.CommuneAr,
                    latitude  = loc.Commune?.CommuneLatitude,
                    longitude = loc.Commune?.CommuneLongitude,
                },
            },
        });
    }

    // ── POST /api/logout ──────────────────────────────────────

    [HttpPost("/api/logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("access_token");
        return Ok(new { success = true, message = "Logged out successfully" });
    }

    // ── GET /api/current_user ─────────────────────────────────
    // fix #1: [Authorize] + User.FindFirst(...) replaces manual GetPrincipalFromCookie(),
    // routing unauthenticated requests through the standard JWT bearer pipeline → 401.

    [HttpGet("/api/current_user")]
    [Authorize]
    public async Task<IActionResult> CurrentUser()
    {
        if (!int.TryParse(User.FindFirstValue("user_id"),    out int userId)    ||
            !int.TryParse(User.FindFirstValue("commune_id"), out int communeId))
            return Unauthorized(new { detail = "Malformed token claims." });

        // fix #7: single joined query for commune → daira → wilaya.
        var loc = await LoadLocationChainAsync(communeId);

        return Ok(new
        {
            id       = userId,
            username = User.FindFirst("username")?.Value,
            name     = User.FindFirst("name")?.Value,
            email    = User.FindFirst("email")?.Value,
            wilaya = new
            {
                id        = loc.Wilaya?.WilayaId,
                name_fr   = loc.Wilaya?.WilayaFr,
                name_ar   = loc.Wilaya?.WilayaAr,
                latitude  = loc.Wilaya?.WilayaLatitude,
                longitude = loc.Wilaya?.WilayaLongitude,
            },
            daira = new
            {
                id        = loc.Daira?.DairaId,
                name_fr   = loc.Daira?.DairaFr,
                name_ar   = loc.Daira?.DairaAr,
                latitude  = loc.Daira?.DairaLatitude,
                longitude = loc.Daira?.DairaLongitude,
            },
            commune = new
            {
                id        = communeId,
                name_fr   = loc.Commune?.CommuneFr,
                name_ar   = loc.Commune?.CommuneAr,
                latitude  = loc.Commune?.CommuneLatitude,
                longitude = loc.Commune?.CommuneLongitude,
            },
        });
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
            join d in db.Dairas  on c.DairaId  equals d.DairaId  into dj
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
}
