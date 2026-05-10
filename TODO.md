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

- [ ] Integrate logging service (`src/lib/errors.ts:265`)

---

## Infrastructure

- [ ] Push Docker images to registry (`make images-push`)
- [ ] Deploy to k8s cluster (`make cluster-up`)
- [ ] CI/CD pipeline
