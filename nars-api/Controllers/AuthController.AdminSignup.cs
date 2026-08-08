using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    // CSRF protection is provided implicitly by the X-Admin-Signup custom header:
    // browsers auto-include cookies on cross-origin requests but cannot set custom
    // headers without JavaScript, so this endpoint is safe from CSRF form attacks.
    // Rate limiting (RateLimitPolicies.Auth) provides additional brute-force protection.

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
        if (!TokenMatches(signupToken, adminSignupOptions.Value.SignupToken))
        {
            return Problem(detail: "Invalid request.", statusCode: 403);
        }

        // 1. Verify the authorizing admin's credentials (timing-safe: the shared
        //    service always runs BCrypt, even for unknown usernames).
        var adminNormalized = body.AdminUsername.ToLowerInvariant();
        var credentialResult = await authorizationService.VerifyCredentialsAsync(
            adminNormalized, body.AdminPassword, MaxFailedAttempts, LockoutMinutes, cancellationToken);

        if (!credentialResult.IsSuccess)
        {
            return credentialResult.Status == CredentialCheckStatus.Locked
                ? Problem(detail: "Admin account is temporarily locked.", statusCode: 423)
                : Problem(detail: "Admin credentials are invalid.", statusCode: 401);
        }

        var admin = credentialResult.User!;

        if (!UserRoles.IsAdmin(admin.Role))
        {
            return Forbid();
        }

        // 3. Validate and create user (role hierarchy, geographic scope, creation,
        //    and persistence are consolidated in the user creation service).
        var creationResult = await userCreationService.CreateUserAsync(
            admin.Role, admin.CommuneId, admin.DairaId, admin.WilayaId,
            body.Name, body.Email, body.Phone, body.Username, body.Password,
            body.Role, body.CommuneId, body.DairaId, body.WilayaId,
            cancellationToken);
        if (!creationResult.IsSuccess)
        {
            return creationResult.IsAuthorizationFailure
                ? Problem(detail: creationResult.Error, statusCode: 403)
                : Problem(detail: creationResult.Error, statusCode: creationResult.StatusCode);
        }

        var newUser = creationResult.User!;

        await refreshService.ResetFailedAttemptsIfNeededAsync(admin, cancellationToken);

        logger.LogInformation(
            "[Auth] {AdminUser} ({AdminRole}) created {NewRole} account {NewUser} via login page",
            admin.Username, admin.Role, newUser.Role, newUser.Username);

        return StatusCode(201, ApiResponse.Ok($"{body.Role} account created successfully."));
    }

    /// <summary>
    /// Constant-time comparison of the signup token. Both inputs are SHA-256
    /// hashed first so token length is not revealed via timing.
    /// </summary>
    private static bool TokenMatches(string? provided, string expected)
    {
        var providedHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(provided ?? string.Empty));
        var expectedHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(expected));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
