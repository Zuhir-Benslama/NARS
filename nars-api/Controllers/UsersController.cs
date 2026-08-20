using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NarsApi.DTOs;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Handles user profile and credential updates.
/// </summary>
[Authorize]
[ApiController]
[Route("/api")]
[Tags("Users")]
public class UsersController(IUserProfileService userProfile, IRefreshTokenService refreshTokens, IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>Updates the authenticated user's username, email, and/or password.</summary>
    [HttpPut("user/profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCredentials([FromBody] UpdateUserRequest body, CancellationToken cancellationToken = default)
    {
        var result = await userProfile.UpdateCredentialsAsync(RequiredCurrentUserId, body, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                CredentialUpdateError.UserNotFound => Problem(detail: "User not found.", statusCode: 404),
                CredentialUpdateError.InvalidUsername or CredentialUpdateError.InvalidEmail or CredentialUpdateError.WeakPassword =>
                    Problem(detail: result.Detail, statusCode: 400),
                CredentialUpdateError.DuplicateUsername or CredentialUpdateError.DuplicateEmail =>
                    Problem(detail: "Username or email already exists.", statusCode: 409),
                CredentialUpdateError.WrongCurrentPassword => Problem(detail: "Current password is incorrect.", statusCode: 403),
                _ => Problem(detail: result.Detail, statusCode: 400),
            };
        }

        if (result.PasswordChanged)
        {
            // Revoke all refresh tokens so sessions using the old password die immediately.
            await refreshTokens.RevokeAllUserTokensAsync(result.User!.Id, cancellationToken);
        }

        return Ok(new UpdateCredentialsResponse(
            Success: true,
            Message: "Profile updated successfully.",
            User: new UserCredentialsInfo(Username: result.User!.Username, Email: result.User.Email)
        ));
    }
}
