# nars-api — Code Quality Issues

All 14 issues fixed. 269 tests passing, build clean.

## Fixed

### Critical
- [x] **Area save + scattered refresh ordering** — guard commune check before saving (`FeaturesController.cs`)
- [x] **Bypassable commune-scope check** — invert logic to require both IDs (`FieldController.cs:169`)
- [x] **Inconsistent DTO validation** — add `[Required]` to `CreateAdminRequest` (`AdminDtos.cs`)

### Major
- [x] **Default role fallback** — empty string instead of commune_user (`NarsControllerBase.cs`)
- [x] **Magic string** — use `FeatureTypes.HouseEntranceLayers.Main` via parameter (`EntranceQueryService.cs`)
- [x] **Silent catch** — log exception in `JsonHelper.DeserializeSafe` (`JsonHelper.cs`)
- [x] **Inefficient in-memory grouping** — use `ToDictionaryAsync` with `Select` (`AdminOverviewService.cs`)
- [x] **No pagination** — add skip/take to inspections endpoint (`FieldController.cs`)
- [x] **Fragile column index** — use `GetOrdinal("total")` instead of magic `7` (`FieldService.cs`)

### Minor
- [x] **Redundant truncation** — remove dead code (`LogsController.cs`)
- [x] **Missing PublicBuilding sub-types** — expose all 40+ layers (`FeatureCatalogController.cs`)
- [x] **Sync File.ReadAllText** — async version with caching (`PagesController.cs`)
- [x] **DisposeAsync double-wait** — remove redundant `await _executingTask` (`BackgroundTaskQueue.cs`)
- [x] **Mutable LastError** — design note, acceptable for scoped usage (`ScatteredAreaService.cs`)
