# Code Quality Issues

## 🔴 High
- [ ] **Module-level mutable state** — 8 files export `let` vars with manual `reset*()` fns: `snapping/snapping.ts`, `rendering/geometry.ts`, `map-init.ts`, `draw/draw-control.ts`, `draw/draw-events.ts`, `draw/draw-marker-patch.ts`, `rotation.ts`, `lib/logger.ts`. Persist across HMR; `resetAllState()` barrel must be manually maintained.
- [ ] **50+ `as` type assertions** across 25+ files — `snap-geometry.ts`, `draw-handlers.ts`, `draw-marker-patch.ts`, `geometry.ts`, `geoman-events.ts`, `edit-commit.ts`, `undo.ts`, `loader-db.ts`, etc. Replace with type guards, Zod schemas, or branded types.
- [ ] **Toast `showConfirm()` uses imperative DOM** — `lib/toast.ts` creates elements via `document.createElement`, bypassing Vue. No ARIA, no focus trap, inline styles.

## 🟠 Medium
- [ ] **Magic numbers** — `draw-save.ts` (100/200ms delays), `draw-control.ts` (50/200/2500ms), `map-init.ts` (zoom 4/18 hardcoded, 10s timeout), `export.ts` (0.92, 15000). Extract to named constants.
- [ ] **Accessibility gaps** — 10+ components: `ToastContainer` (no `aria-live`), `ContextMenu` (no `role="menu"`), `FeatureModal`/`SettingsModal` (no `role="dialog"`, no focus trap), `FieldPanel` tabs (no `role="tab"`), `ProfileMenu` dropdown (no `aria-expanded`).
- [ ] **Duplicated code** — `draw-marker-patch.ts` (2 near-identical patching fns), `edit-commit.ts` (duplicated geometry update), `draw-handlers.ts` (duplicated circle-center calc).
- [ ] **Large functions** — `geoman-events.ts:registerGeomanEvents` (188 lines, 4 untestable handlers), `map-init.ts:initMap` (137 lines), `context-menu.ts:showContextMenu` (89 lines), `map-layers.ts` (309 lines).

## 🔶 Low
- [ ] **Dead exports** — `utils/debug.ts:debugInfo()`, `isDebugEnabled()` — only used in test files, unreferenced in production.
- [ ] **HMR guard inconsistency** — `lib/logger.ts` `batch` array has no `import.meta.hot.dispose` handler.
- [ ] **No integration tests** — no test simulates a multi-step user workflow (e.g., draw → save → verify layer).

## ✅ Clean (passing lint/typecheck/test)
- [x] ESLint `no-console` — 0 violations (debug utility properly exempted)
- [x] ESLint `@typescript-eslint/no-explicit-any` — 0 violations
- [x] ESLint `vue/no-v-html` — 0 violations (DOMPurify used everywhere)
- [x] Prettier — all files formatted
- [x] vue-tsc — 0 type errors
- [x] Stylelint — 0 violations
- [x] Tests — 384 passed, 0 failed
- [x] Build — clean
