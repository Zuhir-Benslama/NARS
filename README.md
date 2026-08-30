# NARS — National Addressing Reference System

A full-stack geographic data management application for urban addressing across hierarchical administrative divisions (wilaya > daira > commune). Built with ASP.NET Core 10 and Vue 3, with a Python ML segmentation service and an Android companion app.

## Architecture

```
┌─────────────────────┐      ┌──────────────────────┐
│   Frontend (SPA)    │      │    Backend (API)     │
│   Vue 3 + Vite      │◄────►│  ASP.NET Core 10     │
│   MapLibre GL JS    │      │  EF Core + PostGIS   │
│   Pinia + vue-i18n  │      │  JWT Auth            │
│   OTel (Web SDK) ───┼─────►│  OTel (.NET SDK) ────┼──┐
└─────────────────────┘      └──────────────────────┘  │
                                                       │
              ┌─────────────────────────────────────────┘
              ▼
    ┌───────────────────────┐
    │  OpenTelemetry        │
    │  Collector            │
    └───────┬───────┬───────┘
            │       │
    ┌───────▼──┐ ┌──▼────────┐
    │  Tempo   │ │  Loki     │
    │ (traces) │ │  (logs)   │
    └───────┬──┘ └──┬────────┘
            │       │
    ┌───────▼───────▼───────┐
    │  Grafana              │
    │  + Prometheus         │
    │  (metrics + UI)       │
    └───────────────────────┘
```

## Repositories

| Project | Description | README | TODO |
|---------|-------------|--------|------|
| `nars-api/` | ASP.NET Core 10 backend API | [`nars-api/README.md`](nars-api/README.md) | [`nars-api/TODO.md`](nars-api/TODO.md) |
| `nars-web/` | Vue 3 + TypeScript web frontend | [`nars-web/README.md`](nars-web/README.md) | [`nars-web/TODO.md`](nars-web/TODO.md) |
| `nars-roads/` | FastAPI segmentation service — aerial tile in, GeoJSON draft features out | — | [`nars-roads/TODO.md`](nars-roads/TODO.md) |
| `nars-infra/` | k8s manifests, init/migration scripts, Dockerfiles | — | — |
| `nars-tests/` | Backend unit & integration tests (Testcontainers) | — | — |

## Quick Start

### Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Node.js 22+** — [Download](https://nodejs.org/)
- **Docker** — for building images
- **kind** — `go install sigs.k8s.io/kind@latest`
- **kubectl** — [Download](https://kubernetes.io/docs/tasks/tools/)
- **kustomize** — `go install sigs.k8s.io/kustomize/kustomize/v5@latest`

### 1. Cluster

```bash
make cluster-up   # Creates kind cluster, builds images, deploys all manifests
make proxy-up     # Bridge host:8080 → cluster ingress (socat + port-forward)
```

The app is available at `http://localhost:8080`. Tear the bridge down with `make proxy-down`.

### 2. Observability Stack (optional)

```bash
make observability-install    # Deploys LGTM + OTel Collector
make observability-port-forward  # Port-forward Grafana, Loki, Tempo
```

### 3. Bootstrap Admin

```bash
make db-admin
```

This generates one-time credentials for the national admin account. Save them — they won't be shown again.

### 4. Access the App

Open `http://localhost:8080` in your browser.

> Grafana credentials are auto-generated. Run `make grafana-password` to retrieve them.

## Makefile Targets

Run `make help` for the full list. The most useful:

| Target | Description |
|--------|-------------|
| `cluster-up` | Full bootstrap: create kind cluster, build images, deploy everything |
| `cluster-down` | Delete kind cluster (auto-backs up data first) |
| `cluster-rebuild` | Delete and recreate the cluster (preserves data) |
| `cluster-status` | Show cluster resources |
| `proxy-up` / `proxy-down` | Start/stop the host → cluster port bridge |
| `smoke-test` | Post-deploy smoke test: verify /health, frontend, API auth |
| `db-admin` | Create national admin with one-time generated credentials |
| `db-backup` / `db-restore` | Dump / restore the PostGIS database |
| `db-shell` | Interactive psql shell inside the postgis pod |
| `db-migrate-nars` | Apply SQL migrations to the deployed DB (idempotent) |
| `test` | Run all backend tests |
| `test-coverage` | Backend tests with coverage thresholds enforced |
| `lint` | Cross-project linting (.NET format + infra linters) |
| `infra-lint` | Lint nars-infra: shell, docker, yaml, python, node, makefile, sql, nginx, tag guard |
| `images-build` / `images-push` | Build / push all Docker images |
| `frontend-update` | Rebuild nars-vite, load into kind, rollout restart |
| `observability-install` | Deploy LGTM stack + OTel Collector |
| `observability-port-forward` | Port-forward Grafana, Loki, Tempo |

## Docker Images

- `zuhirbenslama/nars-postgis:latest` — PostGIS 17-3.5 with NARS init script
- `zuhirbenslama/nars-api:latest` — ASP.NET Core 10 backend
- `zuhirbenslama/nars-vite:latest` — Nginx-served Vue 3 SPA
- `zuhirbenslama/nars-backup:latest` — Scheduled database backup job
- `zuhirbenslama/nars-roads:latest` — FastAPI segmentation service (runs standalone)

## Features

- **Hierarchical Admin Roles**: National > Wilaya > Daira > Commune > Field Worker
- **Field Worker Inspections**: Inspect roads, house entrances, and naming panels
- **Map Drawing & Editing**: Draw areas, districts, roads, buildings with MapLibre GL JS + Geoman
- **ML Draft Features**: Segmentation service turns aerial imagery into draft GeoJSON features for review
- **Feature Validation**: Validate features against administrative boundaries
- **Spatial Queries**: PostGIS-powered proximity and containment queries
- **Multi-language**: English, French, Arabic (i18n)
- **Android Client**: Offline-capable field data collection with Jetpack Compose

## Observability

| Component | Role | Access |
|-----------|------|--------|
| **OpenTelemetry Collector** | Receives traces, metrics, logs | Internal — `otel-collector.observability:4317` |
| **Tempo** | Distributed tracing store | `http://localhost:3200` |
| **Loki** | Log aggregation | `http://localhost:3100` |
| **Prometheus** | Metrics scrape & storage | Internal |
| **Grafana** | Dashboards & visualization | `http://localhost:3000` (auto-generated password, run `make grafana-password`) |

## Security

- JWT Bearer tokens with refresh token rotation
- Per-user security stamps — rotating one (lockout, password change) instantly invalidates issued tokens
- BCrypt password hashing
- CSRF protection for cookie-based auth
- Content Security Policy (CSP) with nonces
- Rate limiting on auth endpoints
- Account lockout after failed attempts

## License

GNU General Public License v3.0 — See [LICENSE](LICENSE) for details.
