# TODO

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

## Field Worker Role — Backend (NARS API)

### UserRoles & Hierarchy
- [ ] Add `FieldWorker = "field_worker"` to `NARS/Infrastructure/UserRoles.cs`
- [ ] Update `CanCreateRole()` in `AuthController.AdminSignup.cs` and `AdminController.cs`: add `(CommuneUser, FieldWorker) => true`
- [ ] Update `AdminController.cs` `[Authorize]` attribute — add `commune_user` to authorized roles for the users endpoint (split class-level vs action-level auth if needed)
- [ ] Add `commune_user → field_worker` creation logic in `AdminController.CreateAdmin()` — field_worker inherits the creator's `commune_id`
- [ ] Update `ValidateGeographicFields()` / `ValidateAdminGeo()` in both `AdminController` and `AuthController.AdminSignup` — field_worker needs no additional geo fields
- [ ] Add `commune_user → field_worker` case in `AuthController.AdminSignup.cs` `ValidateScopeAsync()`

### AdminController Auth Split
- [ ] Decide: move `[Authorize(Roles = "...")]` from class-level to individual actions so commune_user can call `POST /api/admin/users` but not monitoring endpoints

### Feature Scoping Decision
- [ ] Decide: should field_worker features be commune-visible or strictly per-user? (if commune-level, add `commune_id` column to features)

---

## Field Worker Role — Frontend (nars-vite)

- [ ] Add `"field_worker"` to `UserRole` union type in `src/types/user.ts`
- [ ] Update `appStore.ts` `isAdminUser` getter — field_worker returns false
- [ ] Extend `SettingsUsers.vue` to allow commune_user to create field_worker accounts
- [ ] Update role-based location selector in user creation form — commune_user creates field_worker without commune picker (inherits from creator)

---

## Field Worker Role — Android (NARStreet)

- [ ] Add `role` field to `User` data class in `data/model/User.kt`
- [ ] Parse `role` from sign-in response JSON
- [ ] (Future) Add role-specific UI behavior if needed

---

## Infrastructure

- [x] Push Docker images to registry (`make images-push`)
- [x] Deploy to k8s cluster (`make cluster-up`)
- [x] CI/CD pipeline
