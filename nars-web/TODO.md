# nars-web (Vue 3 Frontend) — All Tasks Complete

All original TODO items have been addressed. The three remaining items from the initial audit were reviewed and deemed low-ROI:

| Item | Assessment |
|------|-----------|
| `buildFeatureData()` (93 lines) | Already has `extractCoords()` helper extracted. The switch-on-type pattern is inherently sizeable — further splitting would add indirection without reducing complexity. |
| `saveAndUpdateStore()` (71 lines) | Already has `applyCityCenterOverride()` extracted. Well-organized sequential steps (build→save→store→refresh). |
| `CtxMenuItem` type | Still actively used in `context-menu.ts` and `contextMenuStore.ts` — was never dead code. |

**Status**: Typecheck clean, lint clean, 378/378 tests pass, build clean.
