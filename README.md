# NARS – ASP.NET Core Migration

This is the **ASP.NET Core 10** port of the original FastAPI `main.py`.

---

## Project Structure

```
NarsApi/
├── Program.cs                  ← App entry point, DI, middleware pipeline
├── NarsApi.csproj              ← NuGet packages
├── appsettings.json            ← DB connection string, JWT config
│
├── Data/
│   └── AppDbContext.cs         ← EF Core DbContext
│
├── Models/
│   └── Entities.cs             ← User, Feature, Wilaya, Daira, Commune, CommuneBoundary
│
├── DTOs/
│   └── Dtos.cs                 ← Request / response record types
│
├── Services/
│   └── JwtService.cs           ← JWT create / validate
│
├── Controllers/
│   ├── AuthController.cs       ← /api/signup, /api/signin, /api/logout, /api/current_user
│   ├── LocationsController.cs  ← /api/wilayas, /api/dairas, /api/communes, /api/commune/{id}/boundary
│   ├── FeaturesController.cs   ← /api/save, /api/load, /api/clear, /api/delete/{id}, /api/stats
│   └── PagesController.cs      ← /, /login, /map, /app.js
│
└── wwwroot/                    ← Place your static files here
    ├── login.html
    ├── map_app.html
    └── app.js
```

---

## Configuration

Edit `appsettings.json` (or use environment variables / user-secrets):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=nars_db;Username=postgres;Password=YOUR_PASSWORD"
  },
  "Jwt": {
    "SecretKey": "change-this-secret-key-in-production",
    "ExpiresInMinutes": 1440
  }
}
```

---

## Running the App

```bash
# Restore packages
dotnet restore

# Run (development)
dotnet run

# Or with hot-reload
dotnet watch run
```

The app starts on **http://localhost:5000** (or the port defined in `launchSettings.json`).  
The OpenAPI spec is available at **http://localhost:5000/openapi/v1.json** in development.

---

## FastAPI → ASP.NET Core Mapping

| FastAPI concept | ASP.NET Core equivalent |
|---|---|
| `FastAPI()` app object | `WebApplication` / `Program.cs` |
| `@app.get/post/delete(...)` | `[HttpGet/Post/Delete("...")]` on controller methods |
| `Depends(get_db)` | Constructor DI of `AppDbContext` |
| Pydantic `BaseModel` | C# `record` with `[Required]` annotations |
| `HTTPException` | `return BadRequest/NotFound/Conflict(...)` |
| `response.set_cookie(...)` | `Response.Cookies.Append(...)` |
| `request.cookies.get(...)` | `Request.Cookies["..."]` |
| `CORSMiddleware` | `app.UseCors()` + `AddCors(...)` |
| `StaticFiles` mount | `app.UseStaticFiles()` + `wwwroot/` |
| Lifespan startup | `dbCtx.Database.EnsureCreatedAsync()` in `Program.cs` |
| SQLAlchemy async engine | EF Core `AppDbContext` with Npgsql |
| `ST_AsGeoJSON(...)` | Raw SQL via `db.Database.SqlQueryRaw<T>(...)` |
| `bcrypt` | `BCrypt.Net.BCrypt.HashPassword / Verify` |
| `PyJWT` | `System.IdentityModel.Tokens.Jwt` |

---

## Static Files

Copy your frontend files into `wwwroot/`:

```bash
cp login.html map_app.html app.js NarsApi/wwwroot/
```

> `Boundaries.geojson` is no longer needed — commune boundaries are served live from PostGIS via `GET /api/commune/{id}/boundary`.

---

## Notes

- The PostGIS `ST_AsGeoJSON` call in `LocationsController` uses a raw SQL query, identical in behaviour to the original.
- Cookie auth is preserved: signing in sets an `HttpOnly` `access_token` cookie with a 24-hour TTL, and all protected endpoints read from that cookie.
- CORS is set to `AllowAnyOrigin` to match `allow_origins=['*']`. Tighten this for production.
