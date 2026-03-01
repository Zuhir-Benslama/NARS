using Microsoft.AspNetCore.Mvc;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Serves the HTML pages. Static assets (app.js, app.css) are handled
/// automatically by UseStaticFiles() from wwwroot/ — no explicit routes needed.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class PagesController(JwtService jwt) : ControllerBase
{
    // GET / — redirect to map if authenticated, otherwise to login
    [HttpGet("/")]
    public IActionResult Root()
    {
        var token = Request.Cookies["access_token"];
        if (token is not null && jwt.ValidateToken(token) is not null)
            return Redirect("/map");
        return Redirect("/login");
    }

    // GET /login
    [HttpGet("/login")]
    public IActionResult LoginPage() =>
        PhysicalFile(
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "login.html"),
            "text/html");

    // GET /map — auth-guarded
    [HttpGet("/map")]
    public IActionResult MapPage()
    {
        var token = Request.Cookies["access_token"];
        if (token is null || jwt.ValidateToken(token) is null)
            return Redirect("/login");

        return PhysicalFile(
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "map_app.html"),
            "text/html");
    }
}
