using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;

namespace NarsApi.Controllers;

/// <summary>
/// Handles user profile and credential updates.
/// </summary>
[ApiController]
[Tags("Users")]
public class UsersController(AppDbContext db) : NarsControllerBase
{
    [HttpPut("/api/user/update")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCredentials([FromBody] UpdateUserRequest body)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound(new { detail = "User not found." });

        // Validate username uniqueness if changed
        if (!string.IsNullOrWhiteSpace(body.Username) && body.Username != user.Username)
        {
            if (await db.Users.AnyAsync(u => u.Username == body.Username))
                return Conflict(new { detail = "Username already exists." });
            user.Username = body.Username;
        }

        // Validate email uniqueness if changed
        if (!string.IsNullOrWhiteSpace(body.Email) && body.Email != user.Email)
        {
            if (await db.Users.AnyAsync(u => u.Email == body.Email))
                return Conflict(new { detail = "Email already exists." });
            user.Email = body.Email;
        }

        // Only update password when explicitly provided and valid.
        // Never hash an empty string — that would lock the user out.
        if (!string.IsNullOrEmpty(body.Password))
        {
            var pwdErr = PasswordValidator.Validate(body.Password);
            if (pwdErr is not null)
                return BadRequest(new { detail = pwdErr });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // TOCTOU race: concurrent request claimed the username/email first.
            return Conflict(new { detail = "Username or email already exists." });
        }

        return Ok(new
        {
            success = true,
            message = "Profile updated successfully.",
            user = new { user.Username, user.Email },
        });
    }
}
