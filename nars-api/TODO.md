# nars-api — Code Quality TODO

## High Priority
- [x] **LocationsController — nullable `daira_id` param**: Done.
- [x] **LocationsController — DRY repetition**: Extracted `PaginateAsync`.
- [x] **AdminController.NationalOverview — oversized method**: Extracted to service.

## Medium Priority
- [x] **FeaturesController.ClearFeatures — inefficient**: Single delete per type.
- [x] **FeaturesController.UpdateEntity — non-atomic**: Combined `ExecuteUpdateAsync`.
- [x] **AdminController.GetManageableUsers — repeated projection**: `ToAdminSummary`.
- [x] **CanCreateRole duplicated**: Removed from `AuthController.AdminSignup`.
- [x] **FeatureStatsResponse — JSON naming**: Global camelCase.
- [x] **Test project Directory.Build.props inheritance**: Already works.

## Low Priority
- [x] **SaveFeature — redundant null checks**: Removed dead `CreateEntity`/`AddToDbContext` null checks.
- [x] **ValidateAdminGeo duplication**: Inlined; removed wrapper.
- [x] **FeatureTypeRegistry lambdas → factory method**: `Descriptor<T>`.
- [x] **FeatureStatsService complexity**: Configurable timeout; typed `Guid[]` parameter.
- [x] **Skipped ExecuteUpdateAsync test**: Removed; covered by integration test.

## Testing Gaps
- [x] FeaturesController — 16 unit tests (+4 skipped)
- [x] LocationsController — 11 unit tests
- [x] SpatialController — 8 unit tests
- [x] ValidationController — 17 unit tests
- [x] AdminController — integration tests (30)
