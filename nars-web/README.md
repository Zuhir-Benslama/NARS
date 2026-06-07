# nars-web — Web Frontend

Vue 3 + TypeScript SPA with MapLibre GL JS for the National Addressing Reference System.

## Tech Stack

- **Vue 3.5** — Composition API, `<script setup>`
- **TypeScript 6** — Strict mode, zero `any` in production
- **Vite 8** — Bundler + dev server
- **Pinia 3** — State management
- **MapLibre GL JS 5** — Map rendering
- **@geoman-io/maplibre-geoman-free** — Drawing & editing
- **@turf/turf 7** — GIS operations
- **vue-i18n 11** — Internationalization (en, fr, ar)
- **OpenTelemetry** — Traces via Web SDK
- **Vitest** — Unit tests + coverage
- **ESLint 10 + Prettier** — Linting & formatting

## Project Structure

```
src/
├── api/              HTTP client (apiFetch, CSRF, error handling)
├── components/       Vue SFCs
│   ├── admin/        Admin dashboard widgets
│   ├── inspection/   Field inspection forms
│   ├── modals/       Feature type selectors
│   └── settings/     Settings sub-panels
├── composables/      useApiFetch, useTheme
├── config/           App constants (API, map, snapping, validation)
├── i18n/             English, French, Arabic translations
├── lib/              Utilities (validation, toast, errors, logger, telemetry)
├── map/              MapLibre + Geoman integration
│   ├── core/         Map state, Geoman types
│   ├── draw/         Drawing pipeline (handlers, events, save, state)
│   ├── edit/         Edit mode (state, commit, import, snap)
│   ├── features/     Feature data, save helpers, loaders
│   ├── phases/       Phase navigation & storage
│   ├── rendering/    Map styles, labels, geometry helpers
│   ├── roads/        Road graph, markers, orientation
│   ├── snapping/     Snap state machine, sources, search
│   └── context-menu/ Right-click menus
├── stores/           Pinia stores (app, modal, layer, field)
├── styles/           CSS (theme, modal, phase bar, labels)
├── types/            TypeScript type definitions (10 modules)
└── utils/            Debug logging, HTML sanitization
```

## Development

```bash
# Install
npm install

# Dev server with HMR
npm run dev

# Type checking
npm run typecheck

# Lint (zero-warnings policy)
npm run lint

# Format
npm run format

# Tests
npm run test         # Watch mode
npm run test:run     # Single run
npm run test:coverage

# Build
npm run build

# Build + deploy to NARS backend
npm run build:deploy
```

## Scripts

| Script | Purpose |
|--------|---------|
| `dev` | Vite dev server with HMR |
| `typecheck` | `vue-tsc --noEmit` |
| `lint` | ESLint, `--max-warnings 0` |
| `lint:fix` | ESLint auto-fix |
| `format` | Prettier write |
| `format:check` | Prettier check |
| `test` | Vitest watch |
| `test:run` | Vitest single run |
| `test:coverage` | Vitest with V8 coverage |
| `build` | Typecheck + production build |
| `build:deploy` | Build + copy to `../nars-api/wwwroot/` |
| `audit` | `npm audit --audit-level=high` |

## Testing

- **Vitest 4** + **jsdom** — Unit tests
- **@vue/test-utils** — Component tests
- **@vitest/coverage-v8** — Coverage reports
- 10 test files (1,980 lines)

## Features

- 8-phase mapping pipeline (areas → districts → city center → roads → house entrances → public buildings → public spaces → naming panels)
- Drawing, editing, and validation of geographic features
- Snap-to-feature with configurable thresholds
- Phase-based workflow with validation gates
- Hierarchical admin roles (National, Wilaya, Daira, Commune, Field Worker)
- Field worker inspection forms (road, entrance, naming panel)
- Multi-language UI (English, French, Arabic)
- RTL support for Arabic

## License

GNU General Public License v3.0 — See [LICENSE](../LICENSE) for details.
