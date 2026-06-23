# Code Quality Improvements

All items from the initial code quality check have been addressed.

## Fixed

- **`ScatteredAreaService.cs:135`** — `"type": "areas"` → `FeatureTypes.Area` (`"area"`).
- **`FieldController.cs:119-123`** — Hardcoded strings replaced with `FeatureTypes` constants.
- **`ValidationService.cs`** — Extracted `ExecuteScalarAsync` helper; removed 25 lines of duplicate ADO.NET boilerplate.
- **`Program.cs`** — Split into `ServiceRegistrationExtensions.AddNarsServices(...)` (DI config) and `PipelineExtensions.ConfigureNarsPipelineAsync(...)` (middleware). `Program.cs` reduced from 418 to ~45 lines.

## Not Addressed (no action needed)

- **`FeaturesController.cs:59-64`** — `roadId` extraction duplicates `FeatureTypeRegistry.UpdateHouseEntranceRoadId` but serves a distinct concern (pre-save validation vs post-update sync). Parsing logic is trivial; a shared helper adds indirection for negligible gain.
- **`FeatureRepository.cs:120`** — `await using var handle` correctly captures and disposes the `ConnectionHandle`. No issue.
- **`ValidationController.cs:145`** — `TradActivitiesZone` matches the constant name in `FeatureTypes.cs`. Renaming would require a DB migration.

## Verified

- Build: 0 warnings, 0 errors
- Tests: 176 passed, 0 failed, 4 skipped (same as before)
