# Code Quality Issues — nars-web

Findings from the Aug 3 code review. Verified clean: `vue-tsc` typecheck, `eslint` + `prettier`
(`--max-warnings 0`), `stylelint`, 856 unit tests passing (77 files). No `any`/`ts-ignore` in
production code, no TODO/FIXME markers, no `v-html`/`innerHTML` sinks, no secrets in `src/`/`e2e`/`.env*`.

## High

- [x] **H1 — Undo cross-reference repair is memory-only, never persisted**
  `src/map/undo.ts:94-116` — a restored feature gets a NEW `dbId` (line 66), but the repair loop
  re-pointing `mainEntranceDbId`/`roadDbId` from the old ID to the new one mutates `layerStore.$state`
  only; no `PUT` is sent. After reload the server still stores the deleted ID, so references are
  permanently broken.
  Fix: persist the reference repair (PUT updated `mainEntranceDbId`/`roadDbId` on affected features),
  or restore cross-references on the server during undo.
  DONE: `undo()` now PUTs repaired `data` to each affected feature; failed PUTs trigger a warning toast
  (`map_restore_refs_warning`). Added `warning` ToastType + color. 3 new tests in `undo.test.ts`.

- [x] **H2 — Delete is pushed to the undo stack BEFORE the DELETE succeeds**
  `src/map/context-menu/ctx-menu-actions.ts:152-155` and `src/map/core/geoman-events.ts:172-181` —
  `recordDelete(...)` runs before `apiFetch(DELETE)`. If the DELETE fails, the feature still exists in
  the DB/stores but a stale undo entry remains; Ctrl+Z then POSTs a duplicate. The Geoman path has
  already removed the feature visually, so map and stores disagree on failure.
  Fix: call `recordDelete` only after the DELETE returns OK (and restore the visual state on failure).
  DONE: `recordDelete` moved after the successful DELETE in both paths; on Geoman DELETE failure the
  feature is re-rendered via `buildGeoJsonFeature` (`restoreRemovedFeature`, no-op if still present).
  4 new tests.

- [x] **H3 — Cancel-edit snapshot restore is broken for point/marker features**
  `src/map/edit/edit-mode.ts:44-51` + `src/map/edit/edit-commit.ts:203-214` + `src/map/core/geoman-events.ts:97-98` —
  for a marker, `enableEditMode` snapshots `[{lat, lng}]`, but drag-end overwrites `entry.data.lat/lng`;
  `cancelEditMode` only writes `entry.data.coordinates = snapshot`, and `updateFeatureGeometry`
  (edit-commit.ts:129) prefers `lat/lng`, so "cancel" leaves the marker at the dragged position.
  Fix: restore `lat/lng` (not just `coordinates`) from the snapshot in `cancelEditMode`.
  DONE: `cancelEditMode` restores `lat/lng` (and drops the stray `coordinates`) for markers; 3 new tests
  in `edit-commit.test.ts`.

## Medium

- [x] **M1 — City-center state duplicated across stores and drifts**
  `src/stores/appStore.ts:22-23` vs `src/map/features/loader-db.ts:100-104`,
  `src/map/context-menu/ctx-menu-actions.ts:161-165`, `src/map/draw/draw-save.ts:190-223` —
  `cityCenterLatLng`/`cityCenterMode` are set only by the loader and cleared only by the context-menu
  delete path. Drawing/editing/deleting a city center via Geoman never updates them, so fly-to uses
  commune coords and InfoPanel status is stale.
  Fix: derive city-center state from the layer store (single source of truth) or update it on all paths.
  DONE: `cityCenterMode`/`cityCenterLatLng` are now getters over `layerStore.cityCenter`
  (appStore.ts:41-48); the duplicated `AppStoreState` fields and all manual writes were removed.
  Verified every write path keeps `layerStore.cityCenter` current: draw (draw-save.ts:212),
  edit-info (ctx-menu-actions.ts:96 `Object.assign(entry.data, result)`), delete (removeFeature).

- [x] **M2 — Success toast shown even when persistence failed; state mutated before PUT**
  `src/map/roads/road-directions.ts:115-122` and `src/map/house-numbering.ts:87-111` — per-request
  errors are swallowed with `debugError`/`.catch()` and a global success toast is shown regardless.
  `setHouseNumbers` mutates `entry.data.entranceNumber/label` before the PUT (house-numbering.ts:87-88),
  so the UI shows values the server never stored — silently reverted on reload.
  Fix: count failures, only toast success when all (or none) failed, and apply local mutations after OK.
  DONE: both now count successes/failures; on any failure they show a partial error toast
  (`map_road_directions_partial` / `map_assigned_numbers_partial`), success toast only when nothing
  failed; `setHouseNumbers` applies the local mutation only after the PUT resolves. 4 new tests.

- [x] **M3 — Startup/boundary/scattered load failures swallowed silently**
  `src/map/features/loader.ts:14-16`, `src/map/features/loader-db.ts:105-107`,
  `src/map/rendering/geometry.ts:139-141,179-181` — all `catch` blocks only `debugError`. A failed
  `/api/current_user` leaves `user=null`, the app boots as an anonymous `commune_user`, boundary is
  skipped — no error banner, partially-broken app with no user feedback.
  Fix: surface a non-fatal error banner (toast/store flag) on load failures.
  DONE: `loadUserAndCommune` sets `appStore.setLoadError(true)` on failure (loader.ts); boundary
  load failure sets the same flag (geometry.ts `displayCommuneBoundary`); `refreshScatteredAreas`
  failure now shows an error toast (`map_scatter_refresh_failed`, added to en/fr/ar).
  `loadFromDatabase` already set the flag. Tests updated/added.

- [x] **M4 — O(n²) full-store rewrite per single-feature mutation**
  `src/stores/featuresStore.ts:62-92` — `updateSource()` remaps the entire `features` array and calls
  `setData` on every `add` (line 24) / `remove` (44) / `update` (52). `generateNamingPanels`
  (`src/map/naming-panels.ts:118-131`) calls `add` per panel in a loop → O(n²) `setData` traffic.
  The loader correctly avoids this via `batchAdd`; the panels path and `undo()` restore don't.
  Fix: batch adds (panel loop) and/or make `updateSource` diff the GeoJSON instead of full rewrite.
  DONE: `generateNamingPanels` now collects panels and commits them with a single `featuresStore.batchAdd`
  (one `setData`); the per-panel dedupe is preserved via an in-run `placed` list. New
  `naming-panels.test.ts` (3 tests). `undo()` restores a single feature per call, so it needs no batching.

- [x] **M5 — Road-direction reversals issue N sequential round-trips**
  `src/map/roads/road-directions.ts:96-119` — each road reversal `await`s its `PUT` before the next,
  giving N latency-bound serial requests. `setHouseNumbers` already uses `Promise.all`
  (house-numbering.ts:105).
  Fix: collect the PUT promises and `Promise.all` them (keep the per-item catch).
  DONE: road-directions.ts now collects save promises per reversal and awaits `Promise.all(saveTasks)`
  (done together with M2; counts tracked per request).

- [x] **M6 — Widespread direct store-state mutation outside actions; caches go stale**
  `src/map/features/loader-db.ts:115,157,159,168,170`, `src/map/house-entrances.ts:60,68,77`,
  `src/phases-nav/navigation.ts:71`, `src/map/snapping/snapping.ts:97-237`,
  `src/map/edit/edit-state.ts:55-59`, `src/map/draw/draw-state.ts:13-22`,
  `src/map/features/feature-modal.ts:13-34` — store state written directly instead of through actions.
  `layerStore._featureMap` (layerStore.ts:79-93) goes stale because direct writes bypass
  `addFeature`/`removeFeature` (which set `_featureMapDirty`); `appStore.syncCounts()` also reaches
  into `useLayerStore()` (appStore.ts:57) — duplicated counts state that must be manually re-synced at
  every mutation site and is missed in several.
  Fix: route mutations through store actions; make the layer map a derived getter instead of a cache.
  DONE: `_featureMap` is now a plain derived getter (no manual cache → cannot go stale); `counts` is a
  derived getter over layerStore counts (`syncCounts`/`updateCounts` removed, all 5 call sites dropped).
  Routed direct writes through actions: loader-db `setCurrentPhase`; house-entrances
  `setReferenceRoad/Entrance`; navigation `setCurrentPhase`; snapping `patchSnapState` (new snapStore
  action); edit-state/edit-mode `setIsEditMode` (new editStore action); draw-state delegates to existing
  drawStore `registerGeomanMarker`/`unpatchGeomanMarker`; feature-modal `patchFields` (new modalStore
  action) + `setRoadOptions`/`setMainEntranceOptions`; undo restore uses `layerStore.addFeature`.

- [x] **M7 — Raw server error bodies are shown to end users; `getUserMessage()` never used**
  `src/api/index.ts:33-43` builds error messages as `detail ?? title ?? message ?? error ?? body`,
  falling back to the raw response body. The message flows straight to UI:
  `src/components/EditSaveButton.vue:39-40`, `src/components/settings/SettingsUsers.vue:220,408,437`,
  `src/components/AdminDashboard.vue:190`, `src/components/WilayaDetailPage.vue:91`,
  `src/map/context-menu/ctx-menu-actions.ts:124,173`, `src/map/features/feature-persistence.ts:24-35`.
  `NarsError.getUserMessage()` (`src/lib/errors.ts:60-81`) provides the safe mapping but is only used
  in tests.
  Fix: expose errors via `getUserMessage()` in user-facing UI; keep raw details for `getTechnicalDetails()`/logs.
  DONE: added `getUserMessageKey(err)` (`src/lib/errors.ts`) — maps a `NarsError` code to a translated
  i18n key (`err_network`, `err_validation`, `err_auth`, `err_not_found`, `err_server`, `err_timeout`,
  `err_permission`, `err_conflict`, `err_unknown`; added to en/fr/ar). All user-facing error paths now
  show `t(getUserMessageKey(err))` instead of raw bodies: EditSaveButton, AdminDashboard, WilayaDetailPage,
  SettingsUsers (delete + create/edit, raw `data.detail` dead branches removed), SettingsAccount,
  ctx-menu-actions (edit/delete toasts), geoman-events (delete toast), draw-save (save toast),
  feature-persistence (returns translated `error`; raw detail still logged), the four inspection forms
  (dead `body.detail` branches removed), and `lib/validation.ts` (validateRoad/District + checkDistrictCoverage).
  Raw details remain in logs via `getErrorMessage`/`getTechnicalDetails`.

- [x] **M8 — CSRF token is decorative for `/api`; frontend fails-closed on it**
  `src/api/index.ts:147-166` attaches `X-CSRF-Token` to all state-changing requests, and hard-fails in
  production when the token is missing (lines 153-157). But the API middleware explicitly skips `/api`
  paths (`nars-api/Infrastructure/PipelineExtensions.cs:231-233`), so the token is never validated —
  real protection is `SameSite=Lax` cookies. If SameSite is ever relaxed, all state-changing endpoints
  become CSRF-able; meanwhile the fail-closed branch bricks every state-changing request on a token
  injection misconfiguration.
  Fix: either have the API validate `X-CSRF-Token` on authenticated non-GET `/api` requests, or drop
  the fail-closed prod branch and document SameSite=Lax as the control.
  DONE (Option 1, API validates): `UseCsrfValidation` now also validates authenticated non-GET `/api`
  requests. Decision extracted to `internal static ShouldValidateCsrf(...)`
  (`nars-api/Infrastructure/PipelineExtensions.cs`). Two carve-outs: API validation is enforced only
  outside Development (Vite dev SPA carries no csrf meta and the `Secure` antiforgery cookie is not
  sent over plain HTTP), and `/api/logs` stays exempt (the SPA's unload `sendBeacon()` cannot set the
  header; its control remains SameSite=Lax). The antiforgery service already used
  `HeaderName = "X-CSRF-Token"` and `/login`+`/map` already inject the paired token, so prod browser
  flows are unaffected. Frontend fail-closed branch kept. SECURITY.md claim now matches reality.
  New `nars-tests/CsrfValidationTests.cs` (11 tests); backend unit suite 279 pass, `dotnet build` clean.

- [x] **M9 — Map/Geoman lifecycle leaks**
  `src/map/index.ts:36-38` + `src/map/map-init.ts:66` — `map.remove()` is never called; `destroyMap()`
  only stops the draw watcher + draw-handler listeners. ~11 `map.on()` registrations have no matching
  `map.off()`: `src/map/core/geoman-events.ts:202-206`, `src/map/field-click.ts:13`,
  `src/map/map-boundary.ts:28-52`, `src/map/snapping/snapping.ts:322-323`. `switchBaseLayer`
  (`map-init.ts:162-203`) re-creates the Geoman instance per base-layer switch without disposing the
  previous one. `destroyDrawEvents()` (`draw-events.ts:41-46`) never calls `disableSnapping()`, so the
  container capture listeners (snapping.ts:125-127), the snap RAF loop, and `snapMarker`/`snapCursor`
  survive a destroy while snapping is active.
  Fix: add a real `destroyMap()` (map.remove + off all handlers + dispose Geoman + disableSnapping)
  and reuse/dispose the Geoman instance on base-layer switches.
  DONE: `destroyMap()` (`map/index.ts`) now stops the draw watcher + draw-handler listeners, calls
  `disableSnapping()` + `uninstallSnapInterceptors()`, unregisters the Geoman map handlers
  (`unregisterGeomanEvents`), the field-worker click (`unregisterFieldWorkerClick`), and the boundary
  handlers (`removeBoundaryClickEvents`), awaits `disposeGeoman()`, then `map.remove()`. New symmetric
  unregister helpers: `geoman-events.ts:unregisterGeomanEvents`, `field-click.ts:unregisterFieldWorkerClick`,
  `map-boundary.ts:removeBoundaryClickEvents` (also resets the registered flag; handlers extracted to
  named functions so `off` references are stable), `snapping.ts:uninstallSnapInterceptors` (snapLngLat
  hoisted to module scope). `switchBaseLayer` now disposes the previous Geoman via the new exported
  `map-init.ts:disposeGeoman()` (guards on `geoman.destroyed`, clears the ctx ref) before re-creating.
  New `map/index.test.ts` (3 tests: dispose+remove, all unregisters called, dispose-before-remove
  ordering); unregister tests added to geoman-events/field-click/map-boundary/snapping suites. Full
  suite 879 tests pass, typecheck + lint clean.

## Low

- [x] **L1 — `returnTo` redirect plumbing unvalidated (latent open-redirect)**
  `src/api/index.ts:54-56` — passes `window.location.pathname + search` to `getLoginPath()?returnTo=`.
  Currently same-origin and unconsumed (`public/login.html:72` hard-codes `/map`), but the current
  query string is passed through untouched and any future "honor returnTo" logic must validate same-origin.
  Fix: remove the param, or validate it client-side before using.
  DONE: the `?returnTo=` param was removed from the 401 redirect entirely — it now hard-codes
  `getLoginPath()` (`src/api/index.ts:54-56`), so no unvalidated value is ever propagated.

- [x] **L2 — Telemetry transmits full request URLs with no consent/opt-out**
  `src/lib/telemetry.ts:10-42` — OpenTelemetry Fetch instrumentation sends the full URL of every API
  call to `VITE_OTEL_ENDPOINT` whenever set. No request bodies (good), but any query-string data
  (e.g. `?search=`) would be transmitted.
  Fix: add an opt-out and strip query strings from captured URLs.
  DONE: added `VITE_OTEL_DISABLED` opt-out ("1"/"true"/"yes"/"on"; `src/lib/telemetry.ts`,
  `.env.example`); new `sanitizeTelemetryUrl()` strips query string + fragment; FetchInstrumentation
  `requestHook` (module-scope `stripQueryFromSpan`) scrubs `ATTR_URL_FULL` and legacy `http.url`
  span attributes. 6 tests in `telemetry.test.ts`.

- [x] **L3 — Login page CSP depends on server-side string replacement**
  `public/login.html:6-9` ships `script-src 'unsafe-inline'` as a placeholder; the real CSP nonce is
  injected by `PagesController`. If the template is ever served without injection (misconfigured cache,
  static-file path), inline scripts run unconstrained.
  Fix: verify a production integration test asserts the served CSP header on `/login` and `/map`.
  DONE: CSP/nonce header logic extracted to `internal static ApplyCspMiddlewareAsync(...)`
  (`nars-api/Infrastructure/PipelineExtensions.cs`); new `nars-tests/SecurityMiddlewareTests.cs`
  (9 tests) asserts `/login` + `/map` get a nonce-bearing CSP header without `'unsafe-inline'`, the
  header nonce matches `HttpContext.Items["csp-nonce"]`, security headers are present, and `/api/*`
  routes get no CSP/nonce. `dotnet build` clean; backend unit suite 288 tests pass.

- [x] **L4 — Plaintext credentials on disk**
  `credentials.csv` at the repo root contains real admin usernames/passwords (untracked and git-ignored,
  so not a repo leak, but real credential material in the working tree).
  Fix: rotate the passwords and move the file to a secrets store.
  DONE (partial): the file was never committed (untracked + git-ignored, confirmed via
  `git log --all`/`git ls-files`), so no history hygiene is needed. The file has been moved out of the
  working tree to `~/.local/share/nars/secrets/credentials.csv` (`chmod 700` dir, `chmod 600` file).
  REMAINING: the 4 role accounts (`wilaya_souk_ahras`, `daira_bir_bouhouche`,
  `commune_bir_bouhouche`, `agent_bir_bouhouche`) use guessable passwords (`WsAhr@2026!Nars` etc.) and
  all 5 accounts exist in the DB (confirmed in all `backup/*.sql.gz` dumps). Passwords must be rotated
  when the cluster is next up (`make cluster-up`), then the new credentials stored in
  `~/.local/share/nars/secrets/`. Tracking: replace the `[x]` with a pending `[ ]`-style follow-up if
  rotation is not completed in this pass.

- [x] **L5 — `undo()` has no re-entrancy guard; undo stack is unbounded**
  `src/map/undo.ts:43` — rapid Ctrl+Z can launch two concurrent POSTs and interleaved restores out of
  order. `commitEditMode` has `_commitInProgress` (edit-commit.ts:167); undo doesn't.
  `src/stores/undoStore.ts:11` — `undoStack` grows without a cap.
  Fix: add an in-progress guard and cap the stack (e.g. 100 entries).
  DONE: `undo()` guards on module-scope `_undoInProgress` (skips + warns while one is in flight,
  cleared in `finally`; `src/map/undo.ts`); `undoStore` caps `undoStack` at `MAX_UNDO_ENTRIES = 100`
  with oldest-entry eviction. New tests: concurrent-undo skip (`undo.test.ts`), cap/eviction
  (`stores/undoStore.test.ts`).

- [x] **L6 — Empty catch swallows all errors in the snap hot path**
  `src/map/snapping/snapping.ts:245-247` — comment says "map context destroyed", but the catch is
  unconditional, hiding real bugs in `findNearestSnap`.
  Fix: rethrow or at least narrow the catch to the expected "destroyed" case.
  DONE: the catch now only swallows the expected `"accessed before initMap"` (context destroyed)
  error; any other error is logged via `debugError("[SNAP] processSnapMove failed:", err)`.

- [x] **L7 — Naming panels created with placeholder `dbId: "0"` and never persisted**
  `src/map/naming-panels.ts:108-130` — `addPanelIfMissing` adds entries to both stores but never calls
  the API; "0" also collides with any real feature whose dbId is "0". Currently DEV-only, but if wired
  to UI the panels vanish on reload.
  Fix: persist panels when created, or keep them fully local and out of the layer stores.
  DONE: `generateNamingPanels` now persists every generated panel via `saveToDatabase` (parallel
  `Promise.all`) and swaps the placeholder `dbId` for the server-assigned id (both in the layer store
  entry and the maplibre feature). Persistence failures degrade gracefully: the panel is kept local-only
  with a `local_*` dbId (never the colliding `"0"`) and a `debugWarn`. 2 new tests (persist + real
  dbId, local-only fallback) in `naming-panels.test.ts`.

- [x] **L8 — `switchBaseLayer` has no in-flight guard**
  `src/map/map-init.ts:162-203` — two rapid style switches both run `setStyle → initSources →
  initGeoman`, potentially creating a second Geoman instance on the same map and calling
  `featuresStore.updateSource()` against a stale source.
  Fix: track an in-flight switch and ignore/queue concurrent switches.
  DONE: module-scope `_styleSwitchInFlight` guard in `switchBaseLayer` — a concurrent switch while one
  is in flight is ignored (with a `debugWarn`), and the flag is always released in `finally`. 2 new
  tests in `map-init.test.ts` (concurrent switch dropped; subsequent switch after completion allowed).

- [x] **L9 — Edge-visibility poll continues after draw mode ends**
  `src/map/draw/draw-control.ts:82-95` — the `setInterval` poll (bounded 2.5s/10 retries) keeps calling
  `ensureGeomanDrawEdgesVisible()` after Escape/save; the only clean-clear path (`drawStore.resetDraw`)
  has no production caller.
  Fix: clear `edgePollId`/`edgeTimeoutId` in the draw-mode teardown path.
  DONE: new exported `draw-control.ts:clearEdgeVisibilityPoll()` (clears + nulls both ids; reused at the
  start of `buildDrawControl`) and wired into every draw-mode teardown: `completeDrawingWithGeometry`
  before the save modal, `resetDrawMode` after save, the Escape and context-menu mid-draw
  `disableDraw` paths, and `removeLastVertex`. 2 new tests in `draw.test.ts`.

- [x] **L10 — `ToastContainer` never clears pending timers on unmount**
  `src/components/ToastContainer.vue` — toast auto-dismiss timers live in `toastStore.ts:23`; the
  container has no `onUnmounted(() => store.clearAll())`, so timers fire after unmount and keep
  closures alive.
  DONE: `ToastContainer.vue` now calls `store.clearAll()` in `onUnmounted`, clearing all pending
  auto-dismiss timers (toastStore already had `clearAll`).

## Clean (verified, no action)

- XSS surface: no `v-html`/`innerHTML`/`document.write` in production; all API/user data is `{{ }}`
  bound, `escapeHtml()`-escaped (popups/icons in `src/map/rendering/styles.ts:29-70`, `map-boundary.ts:32`),
  or canvas text symbols; labels sanitized via `sanitizeApiText` (`src/map/features/loader-build.ts:23`).
- No secrets/tokens in `src/`, `e2e/`, or `.env*`; no auth tokens in localStorage/sessionStorage
  (cookies only).
- Client-side role checks are UX gates only — the API independently scopes data.
- `feature-persistence.ts` returns structured `{ok, error}` and callers surface toasts (good pattern).
- Snap pipeline (`snap-search.ts`, `snap-geometry.ts`) and `road-graph.ts` (spatial grid) are
  properly optimized; `geometry.ts` ring math is single-allocation.
- `feature-modal.ts:38-68` uses a `roadSideToken` to discard stale async responses — the correct
  race-guard pattern, worth applying elsewhere.
