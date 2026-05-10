# NARS Frontend (nars-vite)

Map-based geographic data management interface for the National Addressing Reference System.

Built with **Vue 3** + **TypeScript** + **MapLibre GL JS**.

## Prerequisites

- Node.js 22+
- npm

## Setup

```bash
npm install
```

## Development

Start the Vite dev server (proxies `/api` to the .NET backend):

```bash
npm run dev
```

By default the backend is expected at `http://localhost:5000`. Override with:

```bash
VITE_DEV_BACKEND=http://localhost:5000 npm run dev
```

## Scripts

| Script | Purpose |
|---|---|
| `npm run dev` | Start dev server with HMR |
| `npm run build` | Typecheck + production build |
| `npm run typecheck` | Run vue-tsc type checking |
| `npm run lint` | ESLint with zero-warning policy |
| `npm run format` | Prettier auto-format |
| `npm run test:run` | Run Vitest tests (101 tests) |
| `npm run test:coverage` | Run tests with coverage report |
| `npm run build:deploy` | Build and copy to `../NARS/wwwroot/` |

## Tech Stack

- **Vue 3** (Composition API, `<script setup>`)
- **Pinia** for state management
- **MapLibre GL JS** + **@geoman-io/maplibre-geoman-free** for map editing
- **@turf/turf** for GIS operations
- **vue-i18n** for internationalization (EN/FR/AR)
- **Vitest** + **@vue/test-utils** for testing

## Project Structure

```
src/
├── api/           API client & error handling
├── components/    Vue SFC components
├── composables/   Vue composables
├── i18n/          Translations (en, fr, ar)
├── lib/           Utilities (validation, toast, errors)
├── map/           Map engine (10 sub-modules)
├── stores/        Pinia stores
├── styles/        CSS files
├── types/         TypeScript type definitions
└── test/          Test setup & mocks
```
