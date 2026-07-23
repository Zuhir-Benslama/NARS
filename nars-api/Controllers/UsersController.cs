using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Handles user profile and credential updates.
/// </summary>
[ApiController]
[Route("/api")]
[Tags("Users")]
public class UsersController(IUserProfileService userProfile, ILogger<UsersController> logger, IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>Updates the authenticated user's username, email, and/or password.</summary>
    [HttpPut("user/profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCredentials([FromBody] UpdateUserRequest body, CancellationToken cancellationToken = default)
    {
        var user = await userProfile.GetUserByIdAsync(RequiredCurrentUserId, cancellationToken);
        if (user is null)
        {
            return Problem(detail: "User not found.", statusCode: 404);
        }

        // Validate username uniqueness if changed (store normalized lowercase)
        if (!string.IsNullOrWhiteSpace(body.Username))
        {
            var normalized = body.Username.Trim().ToLowerInvariant();
            if (normalized != user.Username)
            {
                if (await userProfile.IsUsernameTakenAsync(normalized, cancellationToken))
                {
                    return Problem(detail: "Username already exists.", statusCode: 409);
                }

                user.Username = normalized;
            }
        }

        // Validate email uniqueness if changed
        if (!string.IsNullOrWhiteSpace(body.Email) && body.Email != user.Email)
        {
            if (await userProfile.IsEmailTakenAsync(body.Email, cancellationToken))
            {
                return Problem(detail: "Email already exists.", statusCode: 409);
            }

            user.Email = body.Email;
        }

        // Only update password when explicitly provided and valid.
        // Never hash an empty string — that would lock the user out.
        if (!string.IsNullOrEmpty(body.Password))
        {
            var pwdErr = PasswordValidator.Validate(body.Password);
            if (pwdErr is not null)
            {
                return Problem(detail: pwdErr, statusCode: 400);
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password);
        }

        try
        {
            await userProfile.UpdateUserAsync(user, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Duplicate username/email on profile update (userId={UserId})", CurrentUserId);
            // TOCTOU race: concurrent request claimed the username/email first.
            return Problem(detail: "Username or email already exists.", statusCode: 409);
        }

        return Ok(new UpdateCredentialsResponse(
            Success: true,
            Message: "Profile updated successfully.",
            User: new UserCredentialsInfo(Username: user.Username, Email: user.Email)
        ));
    }
}
