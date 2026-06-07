# NARS — Backend API

ASP.NET Core 10 API with EF Core + PostGIS for the National Addressing Reference System.

## Tech Stack

- **.NET 10** — ASP.NET Core, Minimal Host, OpenAPI
- **Entity Framework Core 10** — Npgsql + NetTopologySuite (PostGIS)
- **PostgreSQL 17 + PostGIS 3.5** — Spatial database
- **JWT Bearer + Refresh Token Rotation** — Auth
- **BCrypt** — Password hashing
- **OpenTelemetry** — Traces, metrics, logs
- **SonarAnalyzer + NetAnalyzers** — Static analysis

## Project Structure

```
nars-api/
├── Controllers/           # API endpoints
│   ├── AuthController.cs
│   ├── AdminController.cs
│   ├── FeaturesController.cs
│   ├── FieldController.cs
│   ├── ValidationController.cs
│   ├── LocationsController.cs
│   ├── UsersController.cs
│   ├── FeatureCatalogController.cs
│   ├── SpatialController.cs
│   ├── LogsController.cs
│   └── PagesController.cs
├── Data/                  # EF Core DbContext
├── DTOs/                  # Request/response types
├── Infrastructure/        # Services, middleware, helpers
│   ├── Auth/              # JWT, rate limiting, CSP
│   ├── Controllers/       # Base controller
│   └── FeatureTypeRegistry.cs
├── Models/                # Entity models
├── Migrations/            # EF Core migrations
├── Services/              # Business logic
├── Scripts/               # DB seeding
└── wwwroot/               # Built frontend (deployed from nars-web)
```

## API Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/api/signin` | No | Login |
| POST | `/api/signup` | — | **Deprecated (410)** |
| POST | `/api/refresh` | Cookie | Refresh JWT |
| POST | `/api/logout` | Bearer | Logout |
| GET | `/api/current_user` | Bearer | User profile |
| GET | `/api/load` | Bearer | Load all features |
| POST | `/api/save` | Bearer | Save new feature |
| PUT | `/api/update/{id}` | Bearer | Update feature |
| DELETE | `/api/delete/{id}` | Bearer | Delete feature |
| GET | `/api/field/features` | Bearer | Field worker features |
| POST | `/api/field/inspect` | Bearer | Submit inspection |
| GET | `/api/admin/overview` | Bearer | National overview |
| GET | `/api/admin/wilaya/{id}` | Bearer | Wilaya drill-down |
| GET | `/api/admin/daira/{id}` | Bearer | Daira drill-down |

## Development

```bash
# Restore
dotnet restore

# Build with warnings as errors
dotnet build --configuration Release

# Run
dotnet run

# Run tests
dotnet test ../nars-tests

# New migration
dotnet ef migrations add <Name>
```

## Testing

- **xUnit** + **Moq** — Unit tests
- **Testcontainers.PostgreSql** — Integration tests with real PostGIS container
- 10 test files (2,249 lines), 4 integration + 6 unit

```bash
dotnet test ../nars-tests
```

## Security

- JWT with HMAC-SHA256, configurable issuer/audience
- Refresh token rotation (SHA-256 hashed, `SELECT ... FOR UPDATE SKIP LOCKED`)
- BCrypt password hashing
- Account lockout after failed login attempts
- Rate limiting (auth, clear, general API)
- CSP with nonces, CSRF via SameSite cookies
- `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`
- `NuGetAudit` enabled for dependency vulnerability scanning
- Environment variable resolution for DB password and JWT secret

## License

GNU General Public License v3.0 — See [LICENSE](../LICENSE) for details.
