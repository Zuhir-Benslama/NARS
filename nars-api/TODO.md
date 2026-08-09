# NARS API TODO

Code quality issues found during review (build clean, 452 tests passing). Grouped by severity.
All items resolved — build clean, 488 tests passing.

## High

- [x] **IDOR — no commune-scope checks in DraftFeaturesController** (`Controllers/DraftFeaturesController.cs:37,115,146,167`)
  - `ListDrafts` filters only on the caller-supplied `communeId` — any authenticated user can read another commune's draft queue.
  - `SegmentTile` lets any user submit imagery and inject AI drafts into any commune (data poisoning).
  - `AcceptDraft`/`RejectDraft` use the role-only `CanReviewFeatures` policy and never verify the draft's commune against `CurrentCommuneId`. Contrast: `FieldController.cs:133-136` enforces this.
  - **Fix:** extracted `Services/CommuneScopeService.cs` (commune→daira/wilaya hierarchy check) + `Services/DraftFeaturesService.cs`; all draft routes now enforce commune scope against `CurrentCommuneId`.
- [x] **`SegmentTileRequest` validation is a no-op + NRE risk** (`Controllers/DraftFeaturesController.cs:187-205`)
  - `[JsonRequired]` is ignored by `[FromForm]` binding → missing `communeId` silently binds 0, coordinates never rejected.
  - `Tile` has no `[Required]`; `request.Tile.Length` at line 55 throws NRE→500 on a file-less POST.
  - **Fix:** `[FromForm]` binding with `[Required]` on `communeId`/`Tile`, null-guard on the upload, 400 on missing/invalid input.

## Medium

- [x] **Unstable pagination** — `Infrastructure/FeatureQueryHelper.cs:37`, `Services/FieldService.cs:38` order only by non-unique `created_at`; duplicate/skipped rows across pages. Add `id` tiebreaker.
  - **Fix:** `ORDER BY created_at, id` in `FeatureQueryHelper`; `ThenBy(Id)` in `FieldService` and `LocationSearchService`.
- [x] **Background queue silently drops the oldest item** — `Infrastructure/BackgroundTaskQueue.cs:36-39` uses `DropOldest`, so the `TryWrite==false`→LogWarning path is dead code and stale refreshes vanish unlogged.
  - **Fix:** switched to `DropWrite` (rejects the new item, which the caller logs) so the oldest pending refresh is never silently lost.
- [x] **Unbounded singleton state** — `Services/ScatteredAreaService.cs:23` `_lastErrors` dict grows forever for never-retried keys.
  - **Fix:** bounded `ConcurrentDictionary` with a 1000-entry cap; oldest entries evicted on insert; overflow of the generic per-commune error is no longer buffered.
- [x] **Missing `AsNoTracking`** on read-only admin overview queries (`Services/AdminOverviewService.cs:15-42`) — thousands of entities tracked per request.
  - **Fix:** `AsNoTracking()` on all four overview queries.
- [x] **Null element NRE** in `Controllers/LogsController.cs:70` — `{"logs":[null]}` passes `[Required]` on the list.
  - **Fix:** null elements are filtered before use.
- [x] **Email format unvalidated** on 2 of 3 user-management paths (`AuthorizedAdminSignupRequest`, `UpdateUserRequest`); `[MaxLength]`/`[EmailAddress]` on nullable record params are dropped by the C# compiler → invalid/oversized data reaches DB (`Services/UserAuthorizationService.cs:296` → unhandled 500).
  - **Fix:** shared `Infrastructure/UserFieldValidator.cs` (format + max length) applied on admin user creation/update and self `UpdateCredentials`; `FeaturesController` label length too.
- [x] **Deleted roads orphan `house_entrances.road_id`** — `Services/FeatureService.DeleteFeatureAsync:80-96` never cleans up; FKs declared by attribute don't match the schema.
  - **Fix:** road deletes now remove their house entrances + registry rows inside the same transaction.
- [x] **Over-broad `PostgresException → ArgumentException`** (`Services/ValidationService.cs:47-54`) turns disk-full/permission errors into HTTP 400.
  - **Fix:** only SQLSTATE class-22 data exceptions remap to 400; server-side failures (connection, disk, permission) propagate as 500.
- [x] **Duplicated delete-all-features loop** in `Services/FeatureService.cs:107-119` and `Services/UserAuthorizationService.cs:317-326` — extract to one helper.
  - **Fix:** `FeatureTypeRegistry.DeleteAllFeaturesForUserAsync` used by both.
- [x] **Business logic in controllers** — `Controllers/UsersController.UpdateCredentials:32-99` (password policy, hashing, revocation) and `DraftFeaturesController` as a god-object with direct `AppDbContext`.
  - **Fix:** `UpdateCredentials` moved to `IUserProfileService.UpdateCredentialsAsync` (result-based); `DraftFeaturesController` now delegates to `DraftFeaturesService`.

## Low

- [x] Triplicated scattered-refresh trigger (`Controllers/FeaturesController.cs:77,194,220`) — should live in `IFeatureService`.
- [x] `Controllers/AuthController.cs:81-85,132-149` re-implements claim extraction instead of using `CurrentUserId` (already caused drift).
- [x] `Services/SegmentationClient.cs:49` interpolates doubles into URL without `InvariantCulture` (comma-locale breaks it); logs whole response bodies at Error.
- [x] `Infrastructure/PipelineExtensions.cs:54` — startup `CanConnectAsync` has no timeout.
- [x] Static `JsonDocument` never disposed (`Infrastructure/FeatureQueryHelper.cs:17`).
- [x] `DraftFeaturesController` DTOs defined in the controller file instead of `DTOs/`.
