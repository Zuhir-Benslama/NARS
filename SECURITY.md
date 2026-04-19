# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability, please report it responsibly.

## Recently Fixed Security Issues

### JWT Secret Management

- Replaced hardcoded JWT secret in `appsettings.Development.json` with a placeholder
- Added guidance for using dotnet user-secrets for secure secret storage in development

### Logging Improvements

- Removed `Debug.WriteLine` statements that could leak sensitive debug information
- Replaced with proper `logger.LogDebug()` calls using `ILogger<T>` in `AuthController.cs`

### Cache Security Headers

- Static assets served by Vite with hashed filenames now use `Cache-Control: max-age=31536000, immutable`
- HTML pages use `Cache-Control: no-store` to prevent caching sensitive content

### Content Security Policy (CSP)

- Implemented per-request nonce generation stored in `HttpContext.Items["csp-nonce"]`
- Removed `'unsafe-inline'` from script-src CSP policy
- Nonces are injected into HTML pages for legitimate inline scripts

### CSRF Protection

- CSRF protection now covers all endpoints including API endpoints
- Previously, API paths were exempted but this has been corrected
- CSRF tokens are injected via meta tags in HTML pages

### Database Transaction Safety

- Fixed async transaction handling in `FeaturesController.cs`
- Changed `using var tx` to `await using var tx` to ensure proper async disposal
- This prevents potential race conditions and resource leaks

### Dynamic Table Reference Management

- Replaced hardcoded table name list with `FeatureTypeRegistry.GetAllTableNames()`
- Ensures all feature tables are properly handled regardless of configuration

### Frontend Security Improvements

- Removed artificial delays (`setTimeout(r, 100)`) from `src/main.ts`
- Replaced hardcoded Earth radius with configurable `GEOMETRY_CONFIG.earthRadiusMeters`
- Added proper type guards and version comments for GeoJSON handling
- Added JSDoc documentation explaining non-reactive modal state

### Build Artifacts

- Build process now cleans `wwwroot/assets` before copying new files
- Prevents stale hashed files from being served to users