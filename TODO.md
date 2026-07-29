# TODO

## nars-web

### Medium

- [ ] **npm audit: 7 high-severity vulnerabilities in transitive dev deps** — `brace-expansion` DoS via `openapi-typescript` → `@redocly/openapi-core` → `minimatch` and `@vue/test-utils` → `js-beautify` → `editorconfig` → `minimatch`. Fix requires `--force` which downgrades `openapi-typescript` from v7 to v6. Consider pinning or replacing. (`npm audit --audit-level=high`)
- [ ] **Husky installed but not configured** — `husky` v9.1.7 is in `devDependencies` and `lint-staged` is configured (runs eslint + prettier + stylelint on staged files), but `prepare` script is empty and no `.husky/` directory exists. Set up pre-commit hooks to run lint-staged.

### Low

- [ ] **lint-staged runs on `*.{ts,vue}` but not `*.ts` files in `e2e/`** — Playwright test files under `e2e/` are not covered by ESLint or Prettier checks in CI (`npm run lint` only scans `src/`). Add e2e coverage or document as intentional.

## nars-api

### Low

- [ ] **`IDE0330: Use System.Threading.Lock`** — `ScatteredAreaService.cs:18` uses `object` for locking. .NET 10 provides `System.Threading.Lock` for better clarity and potential perf. (`Services/ScatteredAreaService.cs`)
- [ ] **`IDE0059: Unnecessary assignment** — `AuthController.cs:166` assigns to `wilaya` but never reads it. Dead store. (`Controllers/AuthController.cs`)
