using System.Data;
using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Controllers;

/// <summary>
/// Handles account creation that originates from the public login page.
///
/// Creation hierarchy (all enforced here and in AdminController):
///   daira_admin   → may create commune_user (within their own daira only)
///   wilaya_admin  → may create daira_admin  (within their own wilaya only)
///   national_admin → may create wilaya_admin (any wilaya)
///   national_admin → NOT creatable via API; insert directly into the database.
///
/// POST /api/admin/authorized-signup
///   No session required — the authorizing admin's credentials are included
///   in the request body so this can be called from the unauthenticated login page.
/// </summary>
public partial class AuthController
{
    // ── POST /api/admin/authorized-signup ─────────────────────────────────────

    [HttpPost("admin/authorized-signup")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> AuthorizedAdminSignup(
        [FromBody] AuthorizedAdminSignupRequest body)
    {
        // 1. Verify the authorizing admin's credentials.
        // IMPORTANT: always run BCrypt.Verify even when the user is not found.
        // Short-circuiting on "admin is null" leaks whether a username exists
        // via response-time difference (~0 µs vs ~300 ms for a real BCrypt check).
        var admin = await db.Users
            .FirstOrDefaultAsync(u => u.Username == body.AdminUsername);

        // Use a stable dummy hash so BCrypt always does the full work.
        const string _dummyHash = "$2a$11$dummy.constant.time.hash.padding.abcdefghijklmnop";
        var hashToCheck = admin?.PasswordHash ?? _dummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(body.AdminPassword, hashToCheck);

        if (admin is null || !passwordValid)
            return Unauthorized(new { detail = "Admin credentials are invalid." });

        if (!UserRoles.IsAdmin(admin.Role))
            return Forbid();

        // 2. Lockout check.
        if (admin.LockedUntil.HasValue && admin.LockedUntil > DateTime.UtcNow)
            return StatusCode(423, new { detail = "Admin account is temporarily locked." });

        // 3. Role hierarchy.
        if (!CanCreateRole(admin.Role, body.Role))
            return StatusCode(403, new
            {
                detail = $"A {admin.Role} cannot create a {body.Role} account."
            });

        // 4. Geographic scope per role.
        var scopeError = await ValidateScopeAsync(admin, body);
        if (scopeError is not null)
            return StatusCode(403, new { detail = scopeError });

        // 5. Geographic fields present.
        var geoError = ValidateAdminGeo(body);
        if (geoError is not null)
            return BadRequest(new { detail = geoError });

        // 6. Uniqueness.
        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Username == body.Username || u.Email == body.Email);
        if (existing is not null)
        {
            var field = existing.Username == body.Username ? "Username" : "Email";
            return Conflict(new { detail = $"{field} already exists." });
        }

        // 7. Password strength.
        var pwdErr = PasswordValidator.Validate(body.Password);
        if (pwdErr is not null)
            return BadRequest(new { detail = pwdErr });

        // 8. Create.
        var newUser = new User
        {
            Name = body.Name,
            Email = body.Email,
            Phone = body.Phone,
            Username = body.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password),
            Role = body.Role,
            // commune_user gets a CommuneId; admins get their geographic anchor.
            CommuneId = body.Role == UserRoles.CommuneUser ? body.CommuneId : null,
            DairaId = body.Role == UserRoles.DairaAdmin ? body.DairaId : null,
            WilayaId = body.Role == UserRoles.WilayaAdmin ? body.WilayaId : null,
            FailedLoginAttempts = 0,
        };

        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "[Auth] {AdminUser} ({AdminRole}) created {NewRole} account {NewUser} via login page",
            admin.Username, admin.Role, newUser.Role, newUser.Username);

        return StatusCode(201, new
        {
            success = true,
            message = $"{body.Role} account created successfully."
        });
    }

    // ─── Scope validation ─────────────────────────────────────────────────────

    private async Task<string?> ValidateScopeAsync(User admin, AuthorizedAdminSignupRequest body)
    {
        switch (admin.Role, body.Role)
        {
            // daira_admin creates commune_user: commune must belong to admin's daira.
            case (UserRoles.DairaAdmin, UserRoles.CommuneUser):
                {
                    if (!body.CommuneId.HasValue)
                        return "commune_id is required when creating a commune_user.";
                    var commune = await db.Communes.FindAsync(body.CommuneId.Value);
                    if (commune is null)
                        return "Commune not found.";
                    if (commune.DairaId != admin.DairaId)
                        return "That commune does not belong to your daira.";
                    return null;
                }
            // wilaya_admin creates daira_admin: daira must belong to admin's wilaya.
            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                {
                    if (!body.DairaId.HasValue)
                        return "daira_id is required when creating a daira_admin.";
                    var daira = await db.Dairas.FindAsync(body.DairaId.Value);
                    if (daira is null)
                        return "Daira not found.";
                    if (daira.WilayaId != admin.WilayaId)
                        return "That daira does not belong to your wilaya.";
                    return null;
                }
            // national_admin creates wilaya_admin: any wilaya is valid.
            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (body.WilayaId.HasValue && await db.Wilayas.FindAsync(body.WilayaId.Value) is null)
                    return "Wilaya not found.";
                return null;

            default:
                return null;
        }
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Role creation hierarchy:
    ///   daira_admin   → commune_user
    ///   wilaya_admin  → daira_admin
    ///   national_admin → wilaya_admin
    /// national_admin accounts are created directly in the database only.
    /// </summary>
    internal static bool CanCreateRole(string creatorRole, string targetRole) =>
        (creatorRole, targetRole) switch
        {
            (UserRoles.DairaAdmin, UserRoles.CommuneUser) => true,
            (UserRoles.WilayaAdmin, UserRoles.DairaAdmin) => true,
            (UserRoles.NationalAdmin, UserRoles.WilayaAdmin) => true,
            _ => false,
        };

    private static string? ValidateAdminGeo(AuthorizedAdminSignupRequest body) =>
        body.Role switch
        {
            UserRoles.CommuneUser when !body.CommuneId.HasValue => "commune_id is required for commune_user.",
            UserRoles.DairaAdmin when !body.DairaId.HasValue => "daira_id is required for daira_admin.",
            UserRoles.WilayaAdmin when !body.WilayaId.HasValue => "wilaya_id is required for wilaya_admin.",
            UserRoles.NationalAdmin => "national_admin accounts must be created directly in the database.",
            _ => null,
        };
}
