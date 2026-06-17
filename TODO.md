# Code Quality TODO

## Cross-Project Issues

### High Priority
- [x] **LocationsController — nullable `daira_id` param**: `int` → `int?` with 400.
- [x] **LocationsController — DRY repetition**: Extracted `PaginateAsync` generic helper.
- [x] **AdminController.NationalOverview — oversized method**: Delegated to `IAdminOverviewService`.

### Medium Priority
- [x] **FeaturesController.ClearFeatures — inefficient**: Single `ExecuteDeleteAsync` per type.
- [x] **FeaturesController.UpdateEntity — non-atomic**: Combined into one `ExecuteUpdateAsync`.
- [x] **AdminController.GetManageableUsers — repeated projection**: `ToAdminSummary` expression.
- [x] **CanCreateRole duplication**: Shared via `internal static` in `AdminController`.
- [x] **FeatureStatsResponse — JSON naming**: Removed `[JsonPropertyName]`; global camelCase.
- [x] **Test project Directory.Build.props inheritance**: Already inherited (confirmed).

### Low Priority
- [x] **SaveFeature — redundant `CreateEntity` null check**: Type is validated earlier; removed dead check.
- [x] **ValidateAdminGeo duplication**: Inlined; removed private wrapper.
- [x] **FeatureTypeRegistry lambdas → factory method**: `Descriptor<T>` factory removes repetition.
- [x] **FeatureStatsService**: Configurable `CommandTimeoutSeconds`; typed `Guid[]` parameter.
- [x] **Skipped ExecuteUpdateAsync test**: Covered by integration test; removed.
- [ ] Run mutation/coverage analysis on untested edge cases
