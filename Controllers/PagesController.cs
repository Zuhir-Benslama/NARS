using Microsoft.AspNetCore.Mvc;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Serves the HTML pages and static assets, mirroring the FastAPI page routes.
/// Static files (login.html, map_app.html, app.js) should be placed in the wwwroot/ folder.
/// Boundaries are served directly from PostGIS via GET /api/commune/{id}/boundary —
/// the static Boundaries.geojson file is no longer needed.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]   // hide from Swagger
public class PagesController(JwtService jwt) : ControllerBase
{
    // GET /
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
        PhysicalFile(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "login.html"), "text/html");

    // GET /map
    [HttpGet("/map")]
    public IActionResult MapPage()
    {
        var token = Request.Cookies["access_token"];
        if (token is null || jwt.ValidateToken(token) is null)
            return Redirect("/login");

        return PhysicalFile(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "map_app.html"), "text/html");
    }

    // GET /app.js
    [HttpGet("/app.js")]
    public IActionResult ServeJs() =>
        PhysicalFile(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "app.js"), "application/javascript");
}
