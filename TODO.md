# TODO

## ⬆️ HIGHEST PRIORITY — Field Worker Gaps

- [ ] **Wire `registerFieldWorkerClick()`** into `src/map/index.ts` so field workers can click features on the map to inspect them
- [ ] **Verify map layer names** in `src/map/field-click.ts` match actual Maplibre layer IDs used in rendering code
- [ ] **Apply DB migration** `AddInspections` to the database (`dotnet ef database update`)

---

## Field Worker Role — Backend (NARS API) ✅ DONE

### UserRoles & Hierarchy
- [x] Add `FieldWorker = "field_worker"` to `NARS/Infrastructure/UserRoles.cs`
- [x] Update `CanCreateRole()` in `AuthController.AdminSignup.cs` and `AdminController.cs`: add `(CommuneUser, FieldWorker) => true`
- [x] Update `AdminController.cs` `[Authorize]` attribute — add `commune_user` to authorized roles for the users endpoint (split class-level vs action-level auth if needed)
- [x] Add `commune_user → field_worker` creation logic in `AdminController.CreateAdmin()` — field_worker inherits the creator's `commune_id`
- [x] Update `ValidateGeographicFields()` / `ValidateAdminGeo()` in both `AdminController` and `AuthController.AdminSignup` — field_worker needs no additional geo fields
- [x] Add `commune_user → field_worker` case in `AuthController.AdminSignup.cs` `ValidateScopeAsync()`

### AdminController Auth Split
- [x] Split `[Authorize(Roles = "...")]` from class-level to individual actions so commune_user can call `POST /api/admin/users` but not monitoring endpoints

### Feature Scoping Decision
- [x] Field worker features are commune-visible — inspects features by commune_user accounts in the same commune_id

### New: Inspection System
- [x] Create `Inspection` model (`NARS/Models/Inspection.cs`) — `inspections` table with feature_id, user_id, type, jsonb data, status
- [x] Create `FieldController.cs` — endpoints: `GET /api/field/features`, `POST /api/field/inspect`, `GET /api/field/inspections/{id}`, `POST /api/field/entrance/create`
- [x] Create `FieldDtos.cs` — request/response DTOs for inspection endpoints
- [x] Update `AppDbContext.cs` — add Inspections DbSet + indexes
- [x] Migration `AddInspections` — creates the `inspections` table

---

## Field Worker Role — Frontend (nars-vite) ✅ DONE

- [x] Add `"field_worker"` to `UserRole` union type in `src/types/user.ts`
- [x] Update `appStore.ts` `isAdminUser` getter — field_worker returns false
- [x] Extend `SettingsUsers.vue` to allow commune_user to create field_worker accounts
- [x] Update role-based location selector in user creation form — commune_user creates field_worker without commune picker (inherits from creator)
- [x] Create `src/types/inspection.ts` — inspection data types
- [x] Create `src/stores/fieldStore.ts` — selected feature state for field workers
- [x] Create `src/components/FieldPanel.vue` — sidebar panel with tabbed feature list + inspection forms
- [x] Create `src/components/inspection/RoadInspectionForm.vue` — road traffic/trad/lanes/median/vegetation/dead-end/sidewalk form
- [x] Create `src/components/inspection/EntranceInspectionForm.vue` — decision tree: entrance → numbering panel → number → position
- [x] Create `src/components/inspection/NamingPanelInspectionForm.vue` — decision tree: location → panel → naming → position
- [x] Update `App.vue` — field worker gets map + FieldPanel instead of PhaseBar
- [x] Create `src/map/field-click.ts` — map click handler for field worker feature selection
- [x] i18n: Add `su_role_field_worker` and `su_hint_commune` keys to en/fr/ar
- [x] Update AdminDashboard.vue type lookup for field_worker badge

---

## Field Worker Role — Android (NARStreet) ✅ DONE

- [x] Add `role` field to `User` data class in `data/model/User.kt`
- [x] Parse `role` from sign-in response JSON and `current_user` API
- [x] Add `isFieldWorker()` helper to `User` data class
- [x] Add inspection API methods: `submitInspection()`, `createEntranceFromInspection()`, `loadFieldFeatures()`
- [x] Existing `FeatureModal.kt` already has `RoadsValidationFields`, `HouseEntranceValidationFields`, `NamingPanelValidationFields` composables — used by field workers for inspection

---

## maplibre-geoman-android (Kotlin port) — ~87% done

### High Priority
- [ ] **Integration** (0.5-1d)
  - Wire `SourceUpdateManager` into `Features` class
  - Use `PropertyValidators` in feature creation/update
  - Add validation to `addGeoJsonFeature()` and `updateFeature()`
  - Integrate geometry utils into draw modes
  - Add validation error handling to UI
- [ ] **FeatureData enhancement** (1-2d)
  - Parent-child relationships between features
  - Marker management system (add/remove/update)
  - GeoJSON shape feature tracking (`_geoJson` property)
  - Live geometry updates during edit operations
  - Feature cloning/deep copy
- [ ] **Import/Export** (1d)
  - GeoJSON import with shape validation
  - Export with proper formatting
  - Feature type conversion (Point → Marker, etc.)
  - Batch import with error reporting
  - Progress callbacks for large imports
- [ ] **Marker management** (1d)
  - Marker creation with options
  - Marker position updates
  - Marker removal/cleanup
  - Marker event handling (click, drag, etc.)
  - Marker clustering for dense areas

### Medium Priority
- [ ] **Style system** (0.5-1d)
  - Style definitions per shape type
  - Theme support (light/dark)
  - Custom style overrides
  - Style interpolation for zoom levels
- [ ] **Diff tracking** (1-2d)
  - Geometry diff calculation
  - Change history tracking
  - Undo/redo support
  - Change event emission

### Testing
- [ ] Unit tests for new functionality
- [ ] Integration tests with NARS app
- [ ] Memory leak testing (Android specific)
- [ ] Performance testing with 1000+ features
- [ ] Backward compatibility verified

---

## NARStreet (Android app)

### Drawing UI
- [ ] Implement `DrawingControls` composable (polygon, polyline, circle, marker)
- [ ] Implement `FloatingDrawingControls` composable (floating toolbar variant)
- [ ] Edit controls: vertex drag, rotate, scale
- [ ] Action controls: undo, redo, commit, cancel

---

## nars-vite (Web frontend)

- [x] Integrate logging service (`src/lib/errors.ts:265`)

---

## Infrastructure

- [x] Push Docker images to registry (`make images-push`)
- [x] Deploy to k8s cluster (`make cluster-up`)
- [x] CI/CD pipeline
