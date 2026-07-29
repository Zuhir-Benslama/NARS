using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Tests;

public static class AuthTestHelper
{
    /// <summary>Shared test JWT secret key (min 32 chars for HMAC-SHA256).</summary>
    public const string TestJwtSecret = "test-secret-key-that-is-at-least-32-chars-long!!";

    public static ClaimsPrincipal CreateClaimsPrincipal(
        Guid userId, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null,
        string username = "testuser")
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, userId.ToString()),
            new(ClaimNames.Username, username),
            new(ClaimNames.Role, role),
        };

        if (communeId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.CommuneId, communeId.Value.ToString()));
        }

        if (dairaId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.DairaId, dairaId.Value.ToString()));
        }

        if (wilayaId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.WilayaId, wilayaId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    public static void SetUser<T>(T controller, Guid userId, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null, string? username = null)
        where T : ControllerBase => controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreateClaimsPrincipal(userId, role, communeId, dairaId, wilayaId, username ?? "testuser")
            }
        };
}
