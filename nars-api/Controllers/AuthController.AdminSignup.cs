using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Handles account creation that originates from the public login page.
///
/// Creation hierarchy (all enforced here and in AdminController):
///   commune_user  → may create field_worker (inherits the creator's commune_id)
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
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AuthorizedAdminSignup(
        [FromBody] AuthorizedAdminSignupRequest body,
        [FromHeader(Name = "X-Admin-Signup")] string? signupToken,
        CancellationToken cancellationToken = default)
    {
        // Require a custom header to prevent automated scripts from targeting
        // this unauthenticated endpoint. The SPA sets this header on the form.
        if (signupToken != adminSignupOptions.Value.SignupToken)
        {
            return Problem(detail: "Invalid request.", statusCode: 403);
        }

        // 1. Verify the authorizing admin's credentials.
        // IMPORTANT: always run BCrypt.Verify even when the user is not found.
        // Short-circuiting on "admin is null" leaks whether a username exists
        // via response-time difference (~0 µs vs ~300 ms for a real BCrypt check).
        var adminNormalized = body.AdminUsername.ToLowerInvariant();
        var admin = await refreshService.FindUserByUsernameAsync(adminNormalized, cancellationToken);

        var hashToCheck = admin?.PasswordHash ?? DummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(body.AdminPassword, hashToCheck);

        if (admin is null || !passwordValid)
        {
            if (admin is not null && !passwordValid)
            {
                await refreshService.RecordFailedLoginAsync(admin, MaxFailedAttempts, LockoutMinutes, timeProvider.UtcNow, cancellationToken);
            }

            return Problem(detail: "Admin credentials are invalid.", statusCode: 401);
        }

        // 2. Lockout check (after password verify to preserve timing-attack resistance).
        if (admin.LockedUntil.HasValue && admin.LockedUntil > timeProvider.UtcNow)
        {
            return Problem(detail: "Admin account is temporarily locked.", statusCode: 423);
        }

        if (!UserRoles.IsAdmin(admin.Role))
        {
            return Forbid();
        }

        // 3. Role hierarchy.
        if (!authorizationService.CanCreateRole(admin.Role, body.Role))
        {
            return Problem(
                detail: $"A {admin.Role} cannot create a {body.Role} account.",
                statusCode: 403);
        }

        // 4. Geographic scope per role.
        var scopeResult = await authorizationService.ValidateCreateUserScopeAsync(
            admin.Role, admin.DairaId, admin.WilayaId,
            body.Role, body.CommuneId, body.DairaId, body.WilayaId,
            cancellationToken);
        if (scopeResult.Error is not null)
        {
            return Problem(detail: scopeResult.Error, statusCode: 403);
        }

        // 5. Validate and create user (uniqueness, password strength, entity creation).
        var (newUser, error) = await userCreationService.ValidateAndCreateUserAsync(
            body.Name, body.Email, body.Phone, body.Username, body.Password,
            body.Role, body.CommuneId, body.DairaId, body.WilayaId,
            cancellationToken);
        if (error is not null)
        {
            var statusCode = error.Contains("already exists") ? 409 : 400;
            return Problem(detail: error, statusCode: statusCode);
        }

        // 6. Persist (catch DB-level unique constraint races).
        try
        {
            await refreshService.AddUserAsync(newUser!, cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Problem(detail: "A user with that username or email already exists.", statusCode: 409);
        }

        await refreshService.ResetFailedAttemptsIfNeededAsync(admin, cancellationToken);

        logger.LogInformation(
            "[Auth] {AdminUser} ({AdminRole}) created {NewRole} account {NewUser} via login page",
            admin.Username, admin.Role, newUser!.Role, newUser.Username);

        return StatusCode(201, ApiResponse.Ok($"{body.Role} account created successfully."));
    }

}
