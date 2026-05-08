# NARS — National Addressing Reference System

A full-stack geographic data management application for urban addressing across hierarchical administrative divisions (wilaya > daira > commune). Built with ASP.NET Core 10 and Vue 3.

## Architecture

```
┌─────────────────────┐      ┌──────────────────────┐
│   Frontend (SPA)    │      │    Backend (API)     │
│   Vue 3 + Vite      │◄────►│  ASP.NET Core 10     │
│   MapLibre GL JS    │      │  EF Core + PostGIS   │
│   Pinia + vue-i18n  │      │  JWT Auth            │
└─────────────────────┘      └──────────┬───────────┘
                                        │
                              ┌─────────▼──────────┐
                              │   PostgreSQL +      │
                              │   PostGIS           │
                              └────────────────────┘
```

## Prerequisites

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Node.js 22+** — [Download](https://nodejs.org/)
- **PostgreSQL 17+ with PostGIS 3.5** — [Download](https://postgis.net/)

## Quick Start

### 1. Database

```bash
# Start PostgreSQL with PostGIS via Docker
docker run -d --name nars-db \
  -e POSTGRES_USER=nars \
  -e POSTGRES_PASSWORD=changeme \
  -e POSTGRES_DB=nars \
  -p 5432:5432 \
  postgis/postgis:17-3.5
```

### 2. Backend

```bash
cd NARS
cp appsettings.Development.json.example appsettings.Development.json
# Edit appsettings.Development.json with your DB connection string and JWT secret
dotnet run
```

The API will be available at `https://localhost:5001`.

### 3. Frontend

```bash
cd nars-vite
npm install
npm run dev
```

The dev server will be available at `http://localhost:5173`.

### Build & Deploy

```bash
cd nars-vite
npm run build:deploy  # Builds frontend and copies to NARS/wwwroot/
```

Then run the backend — it serves the built frontend as static files.

## Docker

```bash
# Start full stack (API + Frontend + PostgreSQL)
docker compose -f Docker/docker-compose.yml up -d

# Build only the backend image
docker build -f Docker/Dockerfile.nars-api -t nars-api .

# Build only the frontend image
docker build -f Docker/Dockerfile.nars-vite -t nars-vite .
```

## Project Structure

```
Workspace/
├── NARS/                          # Backend — ASP.NET Core 10 API
│   ├── Controllers/               # API endpoints (admin, auth, features, etc.)
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
│   │   ├── api/                   # HTTP client (apiFetch, tests)
│   │   ├── config/                # App constants
│   │   ├── lib/                   # Shared utilities (errors, toast, validation)
│   │   ├── i18n/                  # Internationalization (en, fr, ar)
│   │   ├── store/                 # Legacy Pinia compatibility proxy
│   │   ├── stores/                # Pinia state stores (app, layer, modal)
│   │   ├── types/                 # Domain type definitions
│   │   ├── composables/           # Vue composables (theme, API fetch)
│   │   ├── components/            # Vue SFCs
│   │   │   ├── admin/             # Admin dashboard widgets
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
│   │   │   └── *.ts               # Orchestrator, boundary, numbering, etc.
│   │   ├── styles/                # CSS (theme, modal, phase bar, labels)
│   │   └── utils/                 # Debug logging, HTML sanitization
│   └── ...
│
├── Docker/                        # Docker configs (compose, Dockerfiles)
├── k8s/                           # Kubernetes manifests
├── Scripts/                       # DB setup, admin creation, rendering tools
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

- **Hierarchical Admin Roles**: National > Wilaya > Daira > Commune
- **Map Drawing & Editing**: Draw areas, districts, roads, buildings with MapLibre GL JS + Geoman
- **Feature Validation**: Validate features against administrative boundaries
- **Spatial Queries**: PostGIS-powered proximity and containment queries
- **Multi-language**: English, French, Arabic (i18n)
- **Export**: Print-ready PDF export of map views

## Security

- JWT Bearer tokens with refresh token rotation
- BCrypt password hashing
- CSRF protection for cookie-based auth
- Content Security Policy (CSP) with nonces
- Rate limiting on auth endpoints
- Account lockout after failed attempts

## License

Proprietary — All rights reserved.
