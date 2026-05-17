# NARStreet (Android) — TODO

## P1 — Static Analysis & Build
- [ ] Run detekt — `detekt.yml` configured but never enforced in CI
- [ ] Fix lint build — `espresso-core:3.6.1` unresolvable (blocks `./gradlew lint`)
- [ ] Remove empty source dirs: `domain/`, `store/`, `components/`

## P1 — Test Coverage Gaps
- [ ] Unit tests for `FeatureStore` — pure logic, easy to test (181 lines, zero tests)
- [ ] Unit tests for `PhaseNavigator` — phase advancement validation (123 lines, zero tests)
- [ ] Unit tests for `NarsGeoman` — most complex class (306 lines, zero tests)
- [ ] Unit tests for `FeatureRenderer` — GeoJSON layer rendering (174 lines, zero tests)
- [ ] Unit tests for `GeomanEventHandler` — drawing event handling (217 lines, zero tests)

## P2 — Code Quality Hotspots
- [ ] Decompose `NarsGeoman` — god orchestrator at 306 lines (draw/edit/display/snap/teardown)
- [ ] Convert `ApiService` JSON parsing — replace manual `jsonPrimitive.contentOrNull` with `@Serializable` response classes
- [ ] Specialize `FeatureProperties` — 25 nullable fields is a code smell; consider sealed class per phase
- [ ] Fix inconsistent undo — `FeatureStore.executeUndo()` only handles `Delete`; `Create`/`Update` handled in `MapViewModel`
- [ ] Remove stale `colors.xml` values — Kotlin code never uses `R.color.*`; `Theme.kt` has all colors

## P3 — Nice-to-have
- [ ] Instrumented (Compose UI) tests for map interactions
- [ ] Add `AGENTS.md` with build/test/lint commands for AI-assisted development
- [ ] Cover remaining ViewModel edge cases (sequential undo/redo, concurrent phase changes)
