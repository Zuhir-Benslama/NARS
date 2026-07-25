# TODO

## nars-web

### Low

- [ ] **Inconsistent error handling across components** — mix of `logError`, `debugWarn`, raw message with no unified pattern. Cosmetic.
- [ ] **`draw-save.ts` (364 lines) mixes pure computation with side effects** — separate utilities from orchestration. Refactoring only.
- [ ] **`as unknown as` casts for Geoman internals** — inline casts could break silently on library updates (`src/map/draw/draw-complete.ts:41-46`).

## nars-infra

### Low

- [ ] **Mermaid renderer fetches from CDN at runtime** — `render-mermaid-playwright.mjs` loads mermaid from jsdelivr/unpkg. Supply chain risk if CDN is compromised. (`scripts/render-mermaid-playwright.mjs:50-51`)
- [ ] **PostGIS PV lacks nodeAffinity** — hostPath PV doesn't restrict scheduling to nodes where the path exists. Multi-node clusters will fail. (`k8s/postgis-pv.yaml:18-20`)
- [ ] **CSP allows `'unsafe-inline'` for `style-src`** — Required by Vue 3 SPA runtime style injection. No fix possible without framework change. (`docker/nginx.nars-vite.conf:29`)

## nars-tests

### Medium

- [x] **`TestableRefreshTokenService` hardcodes `FixedUtcNow`** — Subclass overrides `FindRefreshTokenByHashAsync` using `TestData.FixedUtcNow` directly; base class `timeProvider` is inaccessible via primary constructor. (`RefreshTokenServiceTests.cs:56-58`) — added `protected IDateTimeProvider TimeProvider` to base class; subclass now uses `TimeProvider.UtcNow`.
- [x] **`GetDaira_WilayaAdmin_WrongWilaya_ReturnsNotFound` may pass for the wrong reason** — Mocks `GetDairaByIdAsync` but not `GetDairaReportAsync`. If authorization check at `AdminController.cs:67` were removed, test would still return 404. (`AdminControllerTests.cs:200-213`) — added `GetDairaReportAsync` mock returning a valid report so only the auth check produces NotFoundResult.

### Low

- [x] **Three tests discard controller from `CreateController()` with `var (_, db)`** — `ValidateRoad_SharpTurn_ReturnsInvalid`, `ValidateRoad_NotConnected_ReturnsInvalid`, and `ValidateDistrict_Overlap_ReturnsInvalid` each create an unnecessary controller. A `CreateDb()` helper would be cleaner. (`ValidationControllerTests.cs:145,179,253`) — added `CreateDb()` helper; all three now use `var db = CreateDb()`.
