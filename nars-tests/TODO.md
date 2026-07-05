# nars/nars-tests — Code Quality Fixes

All items resolved. Build, style, and tests pass clean.

## What was done

| Issue | Fix |
|---|---|
| **m1** DTO validation gap | `[property: Required(AllowEmptyStrings = false)]` on record DTOs — attribute was landing on ctor param, not property. Unskipped test. |
| **m2** Seed duplication | Extracted `AddAlgerLocations()` helper — `SeedBasicLocationsAsync` and `SeedExtendedLocationsAsync` share it |
| **m3** Claims duplication | Integration tests now use `AuthTestHelper.CreateClaimsPrincipal()` |
| **m4** Missing integration tests | Added `SpatialControllerIntegrationTests` (9 tests) and `FieldControllerIntegrationTests` (6 tests). **Bonus**: exposed and fixed a real bug in `FieldService.QueryFeaturesAsync` — column index for `COUNT(*) OVER()` was 6 instead of 7. |
