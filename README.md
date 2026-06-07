# NARS — National Addressing Reference System

A full-stack geographic data management application for urban addressing across hierarchical administrative divisions (wilaya > daira > commune). Built with ASP.NET Core 10 and Vue 3, with an Android companion app.

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
| `NARS/` | ASP.NET Core 10 backend API | [`NARS/README.md`](NARS/README.md) | [`NARS/TODO.md`](NARS/TODO.md) |
| `nars-vite/` | Vue 3 + TypeScript web frontend | [`nars-vite/README.md`](nars-vite/README.md) | [`nars-vite/TODO.md`](nars-vite/TODO.md) |
| `NARS.Tests/` | Backend unit & integration tests | — | — |
| **New:** `NARStreet/` → moved to [`Zuhir-Benslama/NARStreet`](https://github.com/Zuhir-Benslama/NARStreet) |

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
make cluster-in   # Port-forward to localhost:8080
```

The app is available at `http://localhost:8080`.

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

| Target | Description |
|--------|-------------|
| `cluster-up` | Create kind cluster, build images, deploy everything |
| `cluster-down` | Delete kind cluster |
| `cluster-reload` | Rebuild images, restart deployments |
| `cluster-in` | Port-forward postgis (5433) + frontend (8080) |
| `cluster-logs` | Tail logs from all pods |
| `db-admin` | Create national admin with one-time generated credentials |
| `smoke-test` | Post-deploy smoke test: verify /health, frontend, API |
| `observability-install` | Deploy LGTM stack |
| `observability-port-forward` | Port-forward Grafana, Loki, Tempo |
| `observability-stop` | Stop observability port-forwards |
| `images-build` | Build all Docker images |
| `images-push` | Push all images to Docker Hub |

## Docker Images

- `zuhirbenslama/nars-postgis:latest` — PostGIS 17-3.5 with NARS init script
- `zuhirbenslama/nars-api:latest` — ASP.NET Core 10 backend
- `zuhirbenslama/nars-vite:latest` — Nginx-served Vue 3 SPA

## Features

- **Hierarchical Admin Roles**: National > Wilaya > Daira > Commune > Field Worker
- **Field Worker Inspections**: Inspect roads, house entrances, and naming panels
- **Map Drawing & Editing**: Draw areas, districts, roads, buildings with MapLibre GL JS + Geoman
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
- BCrypt password hashing
- CSRF protection for cookie-based auth
- Content Security Policy (CSP) with nonces
- Rate limiting on auth endpoints
- Account lockout after failed attempts

## License

GNU General Public License v3.0 — See [LICENSE](LICENSE) for details.
