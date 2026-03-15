using Microsoft.AspNetCore.Mvc;
using NarsApi.Data;
using NarsApi.Infrastructure;
using System.Text.Json;

namespace NarsApi.Controllers;

/// <summary>
/// Handles user profile and credential updates.
/// </summary>
[ApiController]
[Tags("Users")]
public class UsersController(AppDbContext db) : NarsControllerBase
{
    [HttpPut("/api/user/update")]
    public async Task<IActionResult> UpdateCredentials([FromBody] JsonElement body)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound();

        if (body.TryGetProperty("username", out var u))
            user.Username = u.GetString() ?? user.Username;

        if (body.TryGetProperty("email", out var e))
            user.Email = e.GetString() ?? user.Email;

        // Security Note: In production, use a library like BCrypt or ASP.NET Identity 
        // to hash passwords. This is a simplified logic.
        if (body.TryGetProperty("password", out var p) && !string.IsNullOrWhiteSpace(p.GetString()))
        {
            // user.PasswordHash = PasswordHasher.Hash(p.GetString());
        }

        await db.SaveChangesAsync();
        return Ok(new { 
            success = true, 
            message = "Profile updated successfully.",
            user = new { user.Username, user.Email }
        });
    }
}