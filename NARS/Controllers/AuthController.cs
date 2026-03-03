using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Tags("Auth")]
public class AuthController(AppDbContext db, JwtService jwt) : ControllerBase
{
    // ── POST /api/signup ──────────────────────────────────────

    [HttpPost("/api/signup")]
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
    public async Task<IActionResult> SignIn([FromBody] SignInRequest body)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == body.Username);
        if (user is null || !BCrypt.Net.BCrypt.Verify(body.Password, user.PasswordHash))
            return Unauthorized(new { detail = "Invalid username or password" });

        var token = jwt.CreateToken(user.Id, user.Username, user.Name, user.Email, user.CommuneId);

        // Set HTTP-only cookie (matches FastAPI behaviour)
        Response.Cookies.Append("access_token", token, new CookieOptions
        {
            HttpOnly    = true,
            MaxAge      = TimeSpan.FromHours(24),
            SameSite    = SameSiteMode.Lax,
            Path        = "/",          // ensure cookie is sent on ALL routes, not just /api/
            IsEssential = true,         // never suppressed by cookie consent middleware
        });

        // Load commune → daira → wilaya chain
        var commune = await db.Communes.FirstOrDefaultAsync(c => c.CommuneId == user.CommuneId);
        var daira   = commune is not null ? await db.Dairas.FirstOrDefaultAsync(d => d.DairaId == commune.DairaId) : null;
        var wilaya  = daira   is not null ? await db.Wilayas.FirstOrDefaultAsync(w => w.WilayaId == daira.WilayaId) : null;

        return Ok(new
        {
            success      = true,
            access_token = token,
            token_type   = "bearer",
            user = new
            {
                id       = user.Id,
                username = user.Username,
                name     = user.Name,
                email    = user.Email,
                commune  = new
                {
                    id        = user.CommuneId,
                    name_fr   = commune?.CommuneFr,
                    name_ar   = commune?.CommuneAr,
                    latitude  = commune?.CommuneLatitude,
                    longitude = commune?.CommuneLongitude,
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

    [HttpGet("/api/current_user")]
    public async Task<IActionResult> CurrentUser()
    {
        var principal = GetPrincipalFromCookie();
        if (principal is null)
            return Unauthorized(new { detail = "Not authenticated" });

        var userId    = int.Parse(principal.FindFirst("user_id")!.Value);
        var communeId = int.Parse(principal.FindFirst("commune_id")!.Value);

        var commune = await db.Communes.FirstOrDefaultAsync(c => c.CommuneId == communeId);
        var daira   = commune is not null ? await db.Dairas.FirstOrDefaultAsync(d => d.DairaId == commune.DairaId) : null;
        var wilaya  = daira   is not null ? await db.Wilayas.FirstOrDefaultAsync(w => w.WilayaId == daira.WilayaId) : null;

        return Ok(new
        {
            id       = userId,
            username = principal.FindFirst("username")?.Value,
            name     = principal.FindFirst("name")?.Value,
            email    = principal.FindFirst("email")?.Value,
            wilaya = new
            {
                id        = wilaya?.WilayaId,
                name_fr   = wilaya?.WilayaFr,
                name_ar   = wilaya?.WilayaAr,
                latitude  = wilaya?.WilayaLatitude,
                longitude = wilaya?.WilayaLongitude,
            },
            daira = new
            {
                id        = daira?.DairaId,
                name_fr   = daira?.DairaFr,
                name_ar   = daira?.DairaAr,
                latitude  = daira?.DairaLatitude,
                longitude = daira?.DairaLongitude,
            },
            commune = new
            {
                id        = communeId,
                name_fr   = commune?.CommuneFr,
                name_ar   = commune?.CommuneAr,
                latitude  = commune?.CommuneLatitude,
                longitude = commune?.CommuneLongitude,
            },
        });
    }

    // ── Helper ────────────────────────────────────────────────

    private System.Security.Claims.ClaimsPrincipal? GetPrincipalFromCookie()
    {
        var token = Request.Cookies["access_token"];
        return token is null ? null : jwt.ValidateToken(token);
    }
}
