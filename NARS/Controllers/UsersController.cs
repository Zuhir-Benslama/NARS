using Microsoft.AspNetCore.Mvc;
using NarsApi.Data;
using NarsApi.DTOs;

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
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCredentials([FromBody] UpdateUserRequest body)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(body.Username))
            user.Username = body.Username;

        if (!string.IsNullOrWhiteSpace(body.Email))
            user.Email = body.Email;

        if (!string.IsNullOrWhiteSpace(body.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password);

        await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Profile updated successfully.",
            user    = new { user.Username, user.Email },
        });
    }
}