# NARStreet (Android) — TODO

## P1 — Static Analysis & Build
- [x] Run detekt — 23 issues fixed (MaxLineLength, TooGenericExceptionCaught, ReturnCount); now clean
- [x] Fix lint build — `espresso-core:3.6.1` inlined in `maplibre-geoman-android/app/build.gradle.kts`
- [x] Remove empty source dirs: `app/src/test/java/.../ui`, `mipmap-*`, `domain/`, `store/`, `components/`

## P1 — Test Coverage Gaps
- [x] Unit tests for `FeatureStore` — 16 tests added (executeUndo for Create/Update/Delete, setReferenceRoad, getCurrentPhaseFeatures, setCurrentPhaseByKey, getFeatureCounts, getFeatureById, selectFeature, removeFeature)
- [x] Unit tests for `PhaseNavigator` — 19 tests exist
- [x] Unit tests for `NarsGeoman` — 27 tests exist
- [x] Unit tests for `FeatureRenderer` — 13 tests exist
- [x] Unit tests for `GeomanEventHandler` — 24 tests exist

## P2 — Code Quality Hotspots
- [x] Decompose `NarsGeoman` — display methods extracted into `FeatureDisplayManager` (173 lines)
- [x] Convert `ApiService` JSON parsing — `saveFeature()`/`createEntranceFromInspection()` use `@Serializable SaveFeatureResponse`/`CreateEntranceResponse`
- [x] Specialize `FeatureProperties` — replaced flat data class with sealed class hierarchy (RoadProperties, HouseEntranceProperties, NamingPanelProperties), removed 11 dead fields
- [x] Fix inconsistent undo — `FeatureStore.executeUndo()` now handles Create (remove), Update (restore old), Delete (re-add)
- [x] Remove stale `colors.xml` values — only `primary` remains (referenced by launcher icons)
- [x] Remove unused `androidx-espresso` dependency from version catalog
- [x] Remove dead espresso resolution strategy from `build.gradle.kts`

## P3 — Nice-to-have
- [x] Instrumented (Compose UI) tests for map interactions — LoginScreen covered (5 tests)
- [x] Compose UI test infrastructure: `androidTest` directory, mockk dependency, Koin test setup
- [x] Add `AGENTS.md` with build/test/lint commands for AI-assisted development
- [x] Cover remaining ViewModel edge cases (sequential undo/redo, concurrent phase changes)
- [x] Fix duplicate operations in ViewModel.undo() — Create/Update paths no longer re-execute the operation already done by FeatureStore.executeUndo()
- [x] Enable HTTPS in nginx for meaningful HSTS — HSTS annotations added to frontend ingress (`max-age=31536000`, `includeSubdomains`); `upgrade-insecure-requests` added to CSP in nginx config
