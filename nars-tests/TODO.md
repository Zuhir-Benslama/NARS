# NARS Tests — Code Quality

## ✅ Fixed (2026-07-01)

- [x] **`SignInRequest_EmptyUsername_ReturnsValidationError` was skipped** — The DTO already has `[Required]` on `Username`; test un-skipped.
- [x] **Missing `.gitignore`** — Added `.gitignore` for build artifacts and IDE files.
- [x] **Empty `TODO.md`** — Populated with this issue tracker.
- [x] **Hardcoded claim name strings in tests** — Replaced `"user_id"`, `"username"`, `"role"`, `"commune_id"`, `"daira_id"`, `"wilaya_id"` string literals with `ClaimNames.*` constants across all 7 test files.
