# NARS — National Addressing Reference System

A full-stack geographic data management application for urban addressing across hierarchical administrative divisions (wilaya > daira > commune). Built with ASP.NET Core 10 and Vue 3.

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
                          ┌────────────────────────────┘
                          ▼
              ┌───────────────────────┐
              │  OpenTelemetry        │
              │  Collector            │
              └───────┬───────┬───────┘
                      │       │
              ┌───────▼──┐ ┌──▼────────┐
              │  Tempo    │ │  Loki     │
              │  (traces) │ │  (logs)   │
              └───────┬──┘ └──┬────────┘
                      │       │
              ┌───────▼───────▼───────┐
              │  Grafana              │
              │  + Prometheus         │
              │  (metrics + UI)       │
              └───────────────────────┘
```

## Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Node.js 22+** — [Download](https://nodejs.org/)
- **Docker** — for building images
- **kind** — `go install sigs.k8s.io/kind@latest`
- **kubectl** — [Download](https://kubernetes.io/docs/tasks/tools/)
- **kustomize** — `go install sigs.k8s.io/kustomize/kustomize/v5@latest`

## Quick Start

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

Grafana: `http://localhost:3000` (`admin`/`admin`).

### 3. Bootstrap Admin

```bash
make db-admin
```

Credentials: `zuhir` / `admin123`.

### 4. Access the App

Open `http://localhost:8080` in your browser.

## Makefile Targets

| Target | Description |
|--------|-------------|
| `cluster-up` | Create kind cluster, build images, deploy |
| `cluster-down` | Delete kind cluster |
| `cluster-reload` | Rebuild images, restart deployments |
| `cluster-in` | Port-forward postgis (5433) + frontend (8080) |
| `cluster-logs` | Tail logs from all pods |
| `db-admin` | Create national admin user (zuhir) |
| `observability-install` | Deploy LGTM stack (Prometheus, Grafana, Loki, Tempo, OTel Collector) |
| `observability-port-forward` | Port-forward Grafana (:3000), Loki (:3100), Tempo (:3200) |
| `observability-stop` | Stop observability port-forwards |
| `images-build` | Build all Docker images |
| `images-push` | Push all images to Docker Hub |

## Docker Images

- `zuhirbenslama/nars-postgis:latest` — PostGIS 17-3.5 with NARS init script
- `zuhirbenslama/nars-api:latest` — ASP.NET Core 10 backend
- `zuhirbenslama/nars-vite:latest` — Nginx-served Vue 3 SPA

## Project Structure

```
Workspace/
├── NARS/                          # Backend — ASP.NET Core 10 API
│   ├── Controllers/               # API endpoints (admin, auth, features, field)
│   ├── Data/                      # EF Core DbContext
│   ├── DTOs/                      # Data transfer objects
│   ├── Infrastructure/            # Services, validators, helpers
│   ├── Models/                    # Entity models
│   ├── Services/                  # Business logic (JWT, password)
│   ├── Scripts/                   # DB seeding and setup scripts
│   └── wwwroot/                   # Built frontend assets (static)
│
├── NARS.Tests/                    # Backend unit tests (xUnit)
│
├── nars-vite/                     # Frontend — Vue 3 + TypeScript SPA
│   ├── src/
│   │   ├── api/                   # HTTP client (apiFetch)
│   │   ├── config/                # App constants
│   │   ├── lib/                   # Shared utilities (errors, toast, validation)
│   │   ├── i18n/                  # Internationalization (en, fr, ar)
│   │   ├── stores/                # Pinia state stores
│   │   ├── types/                 # Domain type definitions
│   │   ├── composables/           # Vue composables
│   │   ├── components/            # Vue SFCs
│   │   │   ├── admin/             # Admin dashboard widgets
│   │   │   ├── inspection/        # Field worker inspection forms
│   │   │   ├── modals/            # Feature type selectors
│   │   │   └── settings/          # Settings sub-panels
│   │   ├── map/                   # MapLibre GL JS + Geoman integration
│   │   │   ├── core/              # Map state, Geoman events, types
│   │   │   ├── draw/              # Drawing lifecycle (8 files)
│   │   │   ├── edit/              # Edit mode (6 files)
│   │   │   ├── roads/             # Road direction algorithm (4 files)
│   │   │   ├── snapping/          # Snap-to-feature system (5 files)
│   │   │   ├── features/          # Feature loading & DB operations (4 files)
│   │   │   ├── rendering/         # Labels, styles, geometry math (3 files)
│   │   │   ├── context-menu/      # Right-click menus (3 files)
│   │   │   ├── phases/            # Phase navigation & storage (2 files)
│   │   │   └── *.ts               # Orchestrator, click handlers, etc.
│   │   ├── styles/                # CSS (theme, modal, phase bar, labels)
│   │   └── utils/                 # Debug logging, HTML sanitization
│   └── ...
│
├── Docker/                        # Dockerfiles
├── k8s/                           # Kubernetes manifests (kustomize)
├── Scripts/                       # DB setup, admin creation
├── docs/                          # SQL schema, documentation, UML diagrams
└── README.md                      # This file
```

### Key Architecture Decisions

- **Map module organization**: Each domain (draw, edit, roads, snapping, etc.) is isolated in its own subfolder with a barrel `index.ts` for clean re-exports
- **Flat barrel re-exports**: `map/index.ts` re-exports the public API, so consumers import from `'./map'` not `'./map/core/state'`
- **lib/ over utils/**: Shared non-domain utilities (errors, toast, validation) are grouped in `lib/`, while `utils/` holds debug and sanitization helpers
- **types/ domain split**: Type definitions are organized by domain (features, modal, user, admin, phases) with a root `types.ts` barrel

## Development

### Running Tests

```bash
# Backend
dotnet test Workspace.sln

# Frontend
cd nars-vite && npm run test:run
```

### Code Quality

```bash
# Backend — build with warnings as errors
dotnet build Workspace.sln --configuration Release

# Frontend — typecheck, lint, format
cd nars-vite
npm run typecheck
npm run lint
npm run format
```

### Pre-commit Hooks

Husky + lint-staged are configured to run ESLint and Prettier on staged files before each commit. Set up with:

```bash
cd nars-vite
npm install  # Runs "husky" via the prepare script
```

## Features

- **Hierarchical Admin Roles**: National > Wilaya > Daira > Commune > Field Worker
- **Field Worker Inspections**: Inspect roads, house entrances, and naming panels
- **Map Drawing & Editing**: Draw areas, districts, roads, buildings with MapLibre GL JS + Geoman
- **Feature Validation**: Validate features against administrative boundaries
- **Spatial Queries**: PostGIS-powered proximity and containment queries
- **Multi-language**: English, French, Arabic (i18n)
- **Export**: Print-ready PDF export of map views

## Observability

The application is instrumented with OpenTelemetry and connected to a full LGTM stack:

| Component | Role | Access |
|-----------|------|--------|
| **OpenTelemetry Collector** | Receives traces, metrics, logs from apps | Internal — `otel-collector.observability:4317` |
| **Tempo** | Distributed tracing store | `http://localhost:3200` |
| **Loki** | Log aggregation | `http://localhost:3100` |
| **Prometheus** | Metrics scrape & storage | Internal |
| **Grafana** | Dashboards & visualization | `http://localhost:3000` (`admin`/`admin`) |

- **Backend** (`NARS/Program.cs`): OTel .NET SDK traces ASP.NET Core, HttpClient, EF Core; metrics from runtime, hosting, Kestrel — all exported via OTLP.
- **Frontend** (`nars-vite/src/lib/telemetry.ts`): OTel Web SDK captures page loads and fetch requests, exported via OTLP/HTTP.
- **Android** MapLibre telemetry is opted out (`AndroidManifest.xml`).

Run `make observability-install && make observability-port-forward` to enable.

## Security

- JWT Bearer tokens with refresh token rotation
- BCrypt password hashing
- CSRF protection for cookie-based auth
- Content Security Policy (CSP) with nonces
- Rate limiting on auth endpoints
- Account lockout after failed attempts

## License

Proprietary — All rights reserved.
