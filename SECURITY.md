# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| latest  | :white_check_mark: |

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please **do not** open a public GitHub issue.

Instead, report it privately via one of the following methods:

- **GitHub private vulnerability reporting:** Use the *"Report a vulnerability"* button on the [Security tab](../../security/advisories/new) of this repository.
- **Email:** Send details to the repository maintainer (see the profile linked in this org).

Please include:
1. A description of the vulnerability and its potential impact.
2. Steps to reproduce (proof of concept if available).
3. Affected versions / files.
4. Any suggested fix or mitigation.

You can expect an initial response within **72 hours** and a resolution or status update within **14 days**.

We will credit reporters in the release notes unless you prefer to remain anonymous.

---

## Known Security Vulnerabilities

This section documents all known security vulnerabilities found in the codebase.

### Critical Severity

#### 1. Hardcoded Database Credentials
- **File:** [`NARS/appsettings.json`](NARS/appsettings.json:3)
- **Issue:** Database password is hardcoded in configuration file
- **Current Value:** `Password=a21305556699`
- **Risk:** Anyone with access to the source code can obtain database credentials
- **Recommendation:** Use environment variables or a secrets management solution (e.g., Kubernetes Secrets, Azure Key Vault, AWS Secrets Manager)

#### 2. Hardcoded JWT Secret Key
- **Files:** 
  - [`NARS/appsettings.json`](NARS/appsettings.json:6)
  - [`k8s/secret.yaml`](k8s/secret.yaml:11)
- **Issue:** JWT secret key is hardcoded with a weak default value
- **Current Value:** `change-this-secret-key-in-production`
- **Risk:** Tokens can be forged if this key is discovered
- **Recommendation:** Generate a cryptographically strong secret (minimum 256-bit) and store securely

---

### High Severity

#### 3. Excessive JWT Token Expiration
- **Files:** 
  - [`NARS/appsettings.json`](NARS/appsettings.json:8)
  - [`NARS/Services/JwtService.cs`](NARS/Services/JwtService.cs:12)
- **Issue:** Access tokens expire after 24 hours (1440 minutes)
- **Risk:** Compromised tokens remain valid for too long
- **Recommendation:** Reduce token expiration to 15-60 minutes. Implement refresh tokens for long sessions

#### 4. Missing JWT Issuer and Audience Validation
- **Files:** 
  - [`NARS/Services/JwtService.cs`](NARS/Services/JwtService.cs:47-48)
  - [`NARS/Program.cs`](NARS/Program.cs:52-53)
- **Issue:** `ValidateIssuer` and `ValidateAudience` are set to `false`
- **Risk:** Tokens issued by any authority are accepted
- **Recommendation:** Enable validation and configure issuer/audience claims

#### 5. Weak SameSite Cookie Setting
- **File:** [`NARS/Controllers/AuthController.cs`](NARS/Controllers/AuthController.cs:69)
- **Issue:** Cookie uses `SameSiteMode.Lax` instead of `SameSiteMode.Strict`
- **Risk:** Cross-site request forgery (CSRF) attacks possible
- **Recommendation:** Use `SameSiteMode.Strict` for authentication cookies

#### 6. Password Update Not Implemented
- **File:** [`NARS/Controllers/UsersController.cs`](NARS/Controllers/UsersController.cs:29-32)
- **Issue:** Password update code is commented out; users cannot change passwords
- **Risk:** Users cannot rotate compromised passwords
- **Recommendation:** Implement password change functionality with proper hashing

---

### Medium Severity

#### 7. No Rate Limiting on Authentication Endpoints
- **Files:** 
  - [`NARS/Controllers/AuthController.cs`](NARS/Controllers/AuthController.cs:22-99)
- **Issue:** `/api/signin` and `/api/signup` endpoints lack rate limiting
- **Risk:** Brute force and credential stuffing attacks
- **Recommendation:** Implement ASP.NET Core rate limiting middleware

#### 8. Wildcard AllowedHosts
- **File:** [`NARS/appsettings.json`](NARS/appsettings.json:19)
- **Issue:** `AllowedHosts: "*"` accepts requests from any host
- **Risk:** Host header injection attacks possible
- **Recommendation:** Specify explicit allowed hosts

#### 9. No Password Strength Requirements
- **File:** [`NARS/DTOs/Dtos.cs`](NARS/DTOs/Dtos.cs:14)
- **Issue:** No validation for password complexity
- **Risk:** Weak passwords can be easily compromised
- **Recommendation:** Add `[MinLength]` and custom validation for password complexity

#### 10. Missing Security Headers
- **File:** [`NARS/Program.cs`](NARS/Program.cs)
- **Issue:** No middleware for security headers (X-Content-Type-Options, X-Frame-Options, CSP, etc.)
- **Risk:** Various client-side attacks (MIME sniffing, clickjacking, XSS)
- **Recommendation:** Add NWebSec or custom middleware for security headers

---

### Low Severity

#### 11. Zero Clock Skew Tolerance
- **File:** [`NARS/Services/JwtService.cs`](NARS/Services/JwtService.cs:49)
- **Issue:** `ClockSkew = TimeSpan.Zero` can cause valid tokens to be rejected
- **Risk:** Authentication failures due to minor clock differences
- **Recommendation:** Use a small tolerance (e.g., 1-5 minutes)

#### 12. Missing Security Event Logging
- **Files:** 
  - [`NARS/Controllers/AuthController.cs`](NARS/Controllers/AuthController.cs)
  - [`NARS/Services/JwtService.cs`](NARS/Services/JwtService.cs)
- **Issue:** Failed login attempts and security events not logged
- **Risk:** Difficulty detecting attack patterns
- **Recommendation:** Implement structured logging for authentication events

#### 13. No Input Length Limits on Search Parameters
- **File:** [`NARS/Controllers/LocationsController.cs`](NARS/Controllers/LocationsController.cs:14-19)
- **Issue:** Search query parameters have no length validation
- **Risk:** Potential denial of service with oversized payloads
- **Recommendation:** Add `[MaxLength]` attribute to search parameters

---

## Security Best Practices Implemented

The following security measures are **correctly implemented** in this codebase:

1. **Password Hashing** - Uses BCrypt for password hashing ([`AuthController.cs:42`](NARS/Controllers/AuthController.cs:42))
2. **HttpOnly Cookies** - Authentication cookie is HttpOnly ([`AuthController.cs:66`](NARS/Controllers/AuthController.cs:66))
3. **Secure Cookies in Production** - Cookie Secure flag set based on environment ([`AuthController.cs:67`](NARS/Controllers/AuthController.cs:67))
4. **Parameterized Queries** - SQL parameters used correctly ([`ValidationController.cs`](NARS/Controllers/ValidationController.cs))
5. **User Data Isolation** - Users can only access their own data ([`FeaturesController.cs`](NARS/Controllers/FeaturesController.cs))
6. **Base Controller Authorization** - All protected endpoints use `[Authorize]` ([`NarsControllerBase.cs`](NARS/Controllers/NarsControllerBase.cs))
7. **Explicit CORS Origins** - Specific origins allowed, not wildcard ([`Program.cs:110-122`](NARS/Program.cs:110-122))

---

## Recommendations Summary

| Priority | Action Item |
|----------|-------------|
| P0 | Move secrets to environment variables/secret management |
| P0 | Generate strong JWT secret |
| P1 | Reduce JWT token expiration |
| P1 | Enable JWT issuer/audience validation |
| P1 | Use SameSite=Strict for auth cookie |
| P1 | Implement rate limiting |
| P2 | Add password strength validation |
| P2 | Add security headers middleware |
| P2 | Implement password change functionality |
| P3 | Add security event logging |
| P3 | Add input length limits |

---

> For an internal log of code-review findings and their resolution, see [`docs/code-review.md`](docs/code-review.md).
