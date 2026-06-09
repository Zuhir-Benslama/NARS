# Contributing to NARS

## Project Overview

NARS (National Addressing Reference System) is a full-stack geospatial application with:

- **nars-api/** — ASP.NET Core 10 backend API (C#, EF Core + PostGIS)
- **nars-web/** — Vue 3 + TypeScript frontend SPA (Maplibre GL JS + Geoman)
- **nars-tests/** — xUnit unit tests + Testcontainers-based integration tests

All code lives under `/home/zuhir/Workspace/`.

---

## Quick Start

### Prerequisites

- **.NET SDK 10.0** — [install](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Node.js 22** — [install](https://nodejs.org/)
- **PostgreSQL 17 + PostGIS 3.5** — local install, Docker, or via `docker compose`
- An editor with C#, TypeScript, and Vue language support (VS Code, Rider, etc.)

### Local Development (No Docker)

```bash
# 1. Start a PostGIS database
docker run -d --name nars-pg \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=nars \
  -p 5432:5432 \
  postgis/postgis:17-3.5

# 2. Initialize the database schema + reference data
psql -h localhost -U postgres -d nars -f nars-infra/docs/nars_db.sql
psql -h localhost -U postgres -d nars -f nars-infra/docs/seed_reference_data.sql

# 3. Run the backend (starts on http://localhost:5000)
cd nars-api
dotnet run

# 4. In another terminal, run the frontend (starts on http://localhost:5173)
cd nars-web
npm install
npm run dev
```

The Vite dev server proxies `/api/*` and `/login` to the backend. Open `http://localhost:5173` in your browser.

### Full Cluster (kind + Kubernetes)

For local Kubernetes deployment with observability, monitoring, and all services:

```bash
# From the workspace root
make cluster-up
```

See the [Makefile](Makefile) for available targets (`cluster-down`, `cluster-rebuild`, `cluster-port-forward`, `smoke-test`, etc.).

---

## Repository Structure

```
.github/workflows/          — CI (backend, frontend, integration, security, Docker build)
nars-infra/                 — Infrastructure configs
├── docker/                 — Dockerfiles for nars-api, nars-web, nars-postgis
├── k8s/                    — Kubernetes manifests (kind-based local cluster)
├── docs/                   — Database schema, UML diagrams, PDF documentation
└── scripts/                — DB creation scripts, admin bootstrap, mermaid tooling
nars-api/                   — ASP.NET Core 10 backend
nars-web/                   — Vue 3 + TypeScript frontend
nars-tests/                 — Backend unit + integration tests (xUnit)
Makefile                    — Cluster management entry point
```

---

## Development Workflow

### Backend (nars-api/)

```
nars-api/
├── Controllers/           — API endpoints
├── Data/                  — EF Core DbContext
├── DTOs/                  — Request/response types
├── Infrastructure/        — Services, middleware, configuration
├── Models/                — Entity models
├── Migrations/            — EF Core schema migrations
├── Services/              — Business logic (JWT, etc.)
```

**Commands:**

```bash
cd nars-api
dotnet restore
dotnet build --configuration Release
dotnet run                              # starts on http://localhost:5000
dotnet watch run                        # hot reload
dotnet test ../nars-tests               # run all tests (unit + integration)
dotnet test ../nars-tests --filter "FullyQualifiedName!~Integration"  # unit only
dotnet ef migrations add <Name>         # add a new migration
dotnet ef database update               # apply migrations
```

**Code style:**
- Nullable reference types enabled, `TreatWarningsAsErrors = true`
- File-scoped namespaces
- Async controller actions returning `ActionResult<T>`
- PascalCase for public members, `_camelCase` for private fields
- `[ProducesResponseType]` on all controller actions

### Frontend (nars-web/)

```
nars-web/src/
├── api/                    — HTTP client (apiFetch, CSRF, error handling)
├── components/             — Vue SFCs (FeatureModal, PhaseBar, InfoPanel, etc.)
├── composables/            — Vue composables (useApiFetch, useTheme)
├── config/                 — Constants (API, map, snapping, validation)
├── directives/             — Custom directives (v-click-outside)
├── i18n/                   — Locale files (en, fr, ar)
├── lib/                    — Utilities (toast, telemetry, validation)
├── map/                    — Maplibre GL + Geoman integration (draw, edit, save)
├── stores/                 — Pinia stores (appStore, layerStore, modalStore, fieldStore)
├── styles/                 — CSS (theme, modal, phase-bar)
├── types/                  — TypeScript interfaces and types
├── test/                   — Test setup and utilities
├── App.vue                 — Root component
└── main.ts                 — Entry point
```

**Scripts** (run from `nars-web/`):

| Command | Purpose |
|---------|---------|
| `npm run dev` | Vite dev server with HMR |
| `npm run build` | Full production build |
| `npm run build:deploy` | Build + copy to `../nars-api/wwwroot/` |
| `npm run typecheck` | TypeScript type checking (`vue-tsc --noEmit`) |
| `npm run lint` | ESLint (zero-warnings policy) |
| `npm run lint:fix` | ESLint auto-fix |
| `npm run format` | Prettier auto-format |
| `npm run test` | Vitest watch mode |
| `npm run test:run` | Vitest single run |
| `npm run test:coverage` | Vitest with coverage thresholds |
| `npm run test:e2e` | Playwright E2E tests |

**Code style:**
- TypeScript strict mode (no `as any` in production code)
- Vue Composition API with `<script setup lang="ts">`
- PascalCase for component names
- No `v-html` (use DOMPurify if HTML rendering is necessary)
- Prettier: no semicolons, double quotes, trailing commas, 100 print width
- ESLint `--max-warnings 0` — zero warnings policy

---

## Testing

### Backend Tests (nars-tests/)

xUnit with Moq for unit tests and Testcontainers.PostgreSql for integration tests.

```bash
# Unit tests only (fast, no database needed)
dotnet test nars-tests --filter "FullyQualifiedName!~Integration"

# Integration tests (requires Docker for Testcontainers)
dotnet test nars-tests --filter "FullyQualifiedName~Integration"

# All tests
dotnet test nars-tests
```

Integration tests spin up a real PostGIS container via Testcontainers. Docker must be running.

### Frontend Tests (Vitest + Playwright)

```bash
# Unit tests (Vitest + jsdom)
npm run test:run

# With coverage (thresholds enforced in CI)
npm run test:coverage

# E2E tests (Playwright, all API calls mocked)
npm run test:e2e
```

The E2E suite mocks all backend API calls via Playwright route interception, so no backend is required. The Vite dev server is started automatically.

### Coverage Thresholds

| Metric | Minimum |
|--------|---------|
| Statements | 15% |
| Branches | 10% |
| Functions | 20% |
| Lines | 15% |

Thresholds are enforced in CI. Add tests when introducing new features or fixing bugs.

---

## CI/CD

The CI pipeline (`.github/workflows/ci.yml`) runs on push to `main`/`develop` and PRs to `main`:

1. **Backend — Build & Test** — `dotnet build` + unit tests
2. **Security — Gitleaks + Audit** — secret scanning + dependency audit
3. **Backend — Integration Tests** — Testcontainers-based DB tests
4. **Frontend — Typecheck, Lint & Test** — typecheck, lint, format check, coverage, build, E2E
5. **Docker Build & Push** — pushes `nars-api`, `nars-vite`, `nars-postgis` images (main/develop only)

All jobs must pass before merging. CodeQL runs on every push and weekly.

---

## Making Changes

### Branching

- Create feature branches from `develop`
- Use the pattern: `feature/<short-description>` or `fix/<short-description>`
- Keep changes focused — one logical change per branch

### Commit Messages

Write clear, conventional commit messages:

```
feat: add wilaya-level admin overview endpoint
fix: handle null commune_id in user creation
refactor: extract geometry validation to shared helper
test: add integration tests for district coverage check
```

### Before Submitting a PR

1. **Backend**: `dotnet build` succeeds, all tests pass
2. **Frontend**: `npm run typecheck && npm run lint && npm run test:run` passes with 0 errors
3. **E2E**: `npm run test:e2e` passes
4. Rebase onto the latest `develop`
5. Write a clear PR description explaining what and why

### Pull Request Process

1. Create the PR against `develop`
2. CI runs automatically — fix any failures
3. Request review from a maintainer
4. Squash-merge to `develop` once approved

---

## Adding Dependencies

### Backend (NuGet)

```bash
cd nars-api
dotnet add package <PackageName> --version <Version>
```

Update `nars-api/NarsApi.csproj` and run `dotnet restore`.

### Frontend (npm)

```bash
cd nars-web
npm install <package>
```

Prefer exact versions. Avoid installing packages that duplicate existing functionality.

---

## Environment Configuration

Copy `.env.example` to `.env` and fill in secrets:

```bash
cp .env.example .env
```

Key variables:
- `POSTGRES_PASSWORD` — database password
- `JWT_SECRET` — JWT signing key (min 32 chars)
- `DOCKER_USERNAME` / `DOCKER_TOKEN` — Docker Hub credentials (for image push)

Backend reads secrets from environment variables first, falling back to `appsettings*.json`. Never commit secrets to the repository.

---

## Docker

Build individual images:

```bash
docker build -f nars-infra/docker/Dockerfile.nars-api -t nars-api .
docker build -f nars-infra/docker/Dockerfile.nars-vite -t nars-vite .
docker build -f nars-infra/docker/Dockerfile.nars-postgis -t nars-postgis .
```

Or use the Makefile for full cluster management.

---

## Code of Conduct

This project follows a standard code of conduct. Be respectful, constructive, and inclusive. Report concerns to the maintainers.

---

## Questions?

Open a discussion on GitHub or reach out to the maintainers.
