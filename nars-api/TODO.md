# Code Quality Issues

All actionable items from the original audit are fixed. Remaining notes for awareness:

## Info / Notes (no code change needed)

- [x] **`FeatureStatsService.cs:68-71`** — Dynamic SQL column aliases use `descriptors[i].Type` as column names. Safe (not user input) but requires PostgreSQL identifier compatibility.
- [x] **`LocationsController.cs:19`** — Intentionally unauthenticated. These endpoints serve public reference data (wilaya/daira/commune lists + boundaries). Confirm no PII is ever exposed through them.
