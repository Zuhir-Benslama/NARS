# nars-web — Code Quality TODO

## Vulnerabilities (npm audit)

- [x] **undici** (high) — TLS certificate validation bypass, information disclosure, HTTP header injection, DoS via WebSocket
- [x] **dompurify** (moderate) — `ALLOWED_ATTR` pollution via `setConfig()` bypass
- [x] **js-yaml** (moderate) — DoS in merge key handling via repeated aliases (via `@redocly/openapi-core`)
