All items from the initial code quality check have been resolved.

## Fixed

- **AdminController.cs** — Fixed 3 `FindAsync` calls that accidentally passed `CancellationToken` as a composite key value (lines 88, 143, 158).
- **AdminController.cs, AuthController.AdminSignup.cs** — Removed unused `using System.Data;` imports.
- **FieldService.QueryFeaturesAsync** — Changed signature from `string tableName` to `FeatureTypeDescriptor descriptor`, moving the trust boundary to the type system so callers can't accidentally pass an arbitrary table name.
- **FieldController.cs** — Updated caller to pass the descriptor instead of `descriptor.TableName`.

## Verified

- Build: 0 warnings, 0 errors
- Tests: 176 passed, 0 failed, 4 skipped (same as before)
