# NARS-Vite Frontend - Component & Architecture Diagram

```mermaid
classDiagram
    %% ===== TYPES =====
    class FeatureData {
        +FeatureTypeKey type
        +string label
        +string? decisionNumber
        +string? decisionDate
        +LatLng[]? coordinates
        +number? lat
        +number? lng
        +number? radius
        +string? areaTypeKey
        +string? districtTypeKey
        +string? roadTypeKey
        +string? roadDbId
        +string? roadLabel
        +left | right? side
        +number? entranceNumber
        +string? mainEntranceDbId
        +string? mainEntranceLabel
        +number? bisNumber
        +string? entranceTypeKey
        +string? spaceTypeKey
        +string? sectorKey
        +string? buildingTypeKey
        +string? geometry
    }

    class LayerEntry {
        +string id
        +string dbId
        +FeatureData data
        +string type
    }

    class MaplibreFeature {
        +string id
        +FeatureData data
        +string type
    }

    class DeletedFeature {
        +LayerEntry entry
        +string phaseKey
    }

    FeatureData o-- LayerEntry
    LayerEntry o-- MaplibreFeature

    %% ===== PINIA STORES =====
    class AppStore {
        State
        +number currentPhase
        +UserInfo? user
        +bool isLoading
        +bool loadError
        +string? referenceRoadDbId
        +string? referenceEntranceDbId
        +bool boundaryEventsRegistered
        Getters
        +isAuthenticated
        +isAdminUser
        +canManageUsers
        +communeName
        +cityCenterLatLng
        +counts (from featuresStore)
        Actions
        +setUser(UserInfo)
        +setLoading(bool)
        +setLoadError(bool)
        +setCurrentPhase(number)
        +setReferenceRoad(dbId)
        +setReferenceEntrance(dbId)
    }

    class FeaturesStore {
        State
        +MaplibreFeature[] features
        Actions
        +add(feature)
        +batchAdd(features)
        +clear()
        +remove(dbId)
        +update(dbId, data)
        +batchUpdate(features)
        +getAll(phase)
        +updateSource()
    }

    class ModalStore {
        State
        +bool visible
        +number phaseIndex
        +bool isEdit
        +string? editDbId
        +string label
        +string? areaTypeKey
        +bool mainUrbanExists
        +string? districtTypeKey
        +string? roadTypeKey
        +string? spaceTypeKey
        +string? sectorKey
        +string? buildingTypeKey
        +number? radius
        +string? decisionNumber
        +string? decisionDate
        +Record~string,string~ errors
        Actions
        +openCreate(phaseIndex, extras?)
        +openEdit(phaseIndex, dbId, existing)
        +close(result?)
        +patchFields(Partial~ModalState~)
    }

    class LayerStore {
        State
        +LayerEntry[] areas
        +LayerEntry[] cityCenter
        +LayerEntry[] districts
        +LayerEntry[] roads
        +LayerEntry[] houseEntrances
        +LayerEntry[] publicBuildings
        +LayerEntry[] publicSpaces
        +LayerEntry[] namingPanels
        Getters
        +mainEntrances
        +secondaryEntrances
        +areaCount, districtCount, roadCount...
        Actions
        +addFeature(LayerEntry)
        +removeFeature(dbId)
        +updateFeature(dbId, data)
        +updateFeatureData(dbId, data)
        +clearLayer(phase)
        +getFeature(dbId)
    }

    class DrawStore {
        State
        +object geomanMarkerPointer
        +object repatchMarkerPointer
        +string? drawingPhase
        +bool savingFeature
        +string? lastPhaseKey
        +number modeSwitchToken
        Getters
        +snappingEnabled (delegates to snapStore)
        Actions
        +registerGeomanMarker(ptr)
        +setSnappingEnabled(bool)
        +setDrawingPhase(phase)
        +setSavingFeature(bool)
    }

    class EditStore {
        State
        +bool isEditMode
        +string? activeGeomanFeatureId
        +LayerEntry? activeEditEntry
        +LatLng[]? activeEditCoordsSnapshot
        +number? draggedVertexIndex
        Actions
        +setIsEditMode(bool)
        +setActiveGeomanFeatureId(id)
        +setActiveEditEntry(entry)
    }

    class SnapStore {
        State
        +bool snappingEnabled
        +bool crosshairActive
        +bool snapActive
        +LatLng? snapLatLng
        +bool snapFrozen
        Actions
        +setSnappingEnabled(bool)
        +setEditDragActive(bool)
        +patchSnapState(partial)
    }

    class UndoStore {
        State
        +DeletedFeature[] undoStack
        Actions
        +recordDelete(entry, phaseKey)
        +popUndo()
        +shiftUndo()
    }

    class SelectionStore {
        State
        +string? selectedFeatureDbId
        Actions
        +setSelectedFeatureDbId(dbId)
    }

    class RotationStore {
        State
        +number currentBearing
        Actions
        +setBearing(deg)
        +resetRotation()
    }

    class FieldStore {
        State
        +SelectedFeature? selectedFeature
        Getters
        +hasSelection
        +featureType
        Actions
        +selectFeature(feature)
        +clearSelection()
    }

    class ToastStore {
        State
        +ToastItem[] toasts
        +number nextId
        Actions
        +addToast(message, type)
        +removeToast(id)
        +clearAll()
    }

    class ConfirmStore {
        State
        +bool visible
        +string message
        +string okText
        Actions
        +show(message) Promise~bool~
        +confirm()
        +cancel()
    }

    class ContextMenuStore {
        State
        +bool visible
        +number x, y
        +CtxMenuItem[] items
        Actions
        +show(x, y, items)
        +hide()
    }

    %% ===== API SERVICE =====
    class ApiModule {
        +apiFetch(path, options)
        +refreshSession()
        +apiUrl(path)
        -applyCSRF(options)
        -classifyError(response)
    }

    %% ===== MAP MODULES =====
    class MapContext {
        +Map map
        +Geoman? geoman
        +boundariesSource?
        +featuresSource?
        +endpointsSource?
        +Popup? popup
    }

    class MapInit {
        +initMap()
        +destroyMap()
        +setBaseLayer(key)
    }

    class DrawEvents {
        +registerDrawEvents()
        +destroyDrawEvents()
    }

    class DrawSave {
        +completeDrawingWithGeometry(geometry, drawType, featureData)
        +normalizeGeometry(geometry, drawType)
        +getFeatureStyle(phase, modalResult)
    }

    class DrawControl {
        +buildDrawControl(phase)
        +resetDrawControl()
    }

    class DrawHandlers {
        +registerDrawHandlers()
        +destroyDrawHandlers()
        +pointToSegmentDist(...)
    }

    class EditMode {
        +enableEditMode(featureId?)
        +commitEditMode()
        +cancelEditMode()
    }

    class Snapping {
        +enableSnapping()
        +disableSnapping()
        +findNearestSnap(cursorX, cursorY, phaseKeys)
        +resetSnapping()
        +enableCrosshair()
        +disableCrosshair()
        +installSnapInterceptors()
    }

    class SnapSearch {
        +findNearestSnap(...)
        +mergeExternalSnapWithDrawFirstVertex(...)
    }

    class SnapGeometry {
        +closestOnCirclePerimeter(...)
        +closestOnSegmentProjected(...)
        +pixelDist(...)
    }

    class FeatureDataModule {
        +featureDataToGeometry(data, kind)
        +buildFeatureData(geometry, phase, modalResult)
        +toApiSaveShape(fd)
    }

    class FeaturePersistence {
        +saveToDatabase(featureData)
    }

    class FeatureLoader {
        +loadFromDatabase()
        +loadUserAndCommune()
    }

    class PhaseNav {
        +navigatePhase(direction)
        +goToPhase(target)
        +setPhase(index)
        +savePhase(index)
        +loadPhase()
    }

    class HouseNumbering {
        +setHouseNumbers()
    }

    class Undo {
        +recordDelete(entry, phaseKey)
        +undo()
    }

    class RoadDirections {
        +computeAndApplyRoadDirections()
        +updateEndpointMarkers()
    }

    class RoadGraph {
        +buildConnectionGraph(roads)
    }

    class RoadOrient {
        +orientFromCityCenter(center, radius, graph, segs, visited)
        +geographicDirection(seg)
    }

    class Labels {
        +refreshLayerVisibility()
    }

    class Geometry {
        +displayCommuneBoundary(communeId)
        +pointInMunicipalLimit(lat, lng)
        +pointInScatteredArea(lat, lng)
        +clearScatteredAreas()
        +addScatteredArea(geoJsonStr)
        +computeCircleRing(lat, lng, radiusMeters)
        +haversineDistance(lat1, lng1, lat2, lng2)
    }

    class ContextMenu {
        +showContextMenu(x, y, dbId, phaseKey)
        +bindContextMenu(e, dbId, phaseKey)
        +showMapContextMenu(x, y, phase)
    }

    class NamingPanels {
        +generateNamingPanels()
    }

    %% ===== COMPOSABLES =====
    class UseTheme {
        +theme
        +setTheme(value)
        +initTheme()
    }

    class UseWindowKeydown {
        +useWindowKeydown(keyMap, enabled?)
    }

    class UseFeatureValidation {
        +validate()
        +buildModalResult()
        +isMainUrban
        +isCityCenter
    }

    class UseFocusTrap {
        +useFocusTrap(containerRef, isActive)
    }

    %% ===== COMPONENTS =====
    class App {
        Root Component
        Role-based routing
    }

    class PhaseBar {
        8-phase stepper
        +goToPhase(index)
    }

    class InfoPanel {
        Feature count display
    }

    class ProfileMenu {
        User profile dropdown
        +settings
        +logout
    }

    class TileControl {
        Base layer switcher
    }

    class FeatureModal {
        Phase-specific form
        +openCreate()
        +openEdit()
        +validate()
        +save()
    }

    class SettingsModal {
        Tabbed settings dialog
    }

    class SettingsGeneral {
        Theme/language settings
    }

    class SettingsAccount {
        Profile/password changes
    }

    class SettingsUsers {
        User CRUD + role admin
    }

    class SettingsFeatures {
        Feature type labels
    }

    class SettingsAbout {
        Version info
    }

    class AdminDashboard {
        Role-based monitoring
    }

    class AreaTypeSelector {
        Central/Secondary urban
    }

    class BuildingTypeSelector {
        Cascading sector->type
    }

    class StatPill {
        Statistic display
    }

    class DairaList {
        Daira report list
    }

    class CommuneList {
        Commune report list
    }

    class ContextMenuCmp {
        Right-click context menu
        Edit / Delete actions
    }

    class EditSaveButton {
        Commit/cancel edit button
    }

    class FieldPanel {
        Field-worker feature feed
    }

    class RoadInspectionForm {
        Road inspection
    }

    class EntranceInspectionForm {
        Entrance inspection
    }

    class NamingPanelInspectionForm {
        Naming panel inspection
    }

    class ToastContainer {
        Toast notifications
    }

    class ConfirmDialogCmp {
        Confirmation dialogs
    }

    class WilayaDetailPage {
        Wilaya drill-down
    }

    %% ===== COMPONENT -> STORE/COMPOSABLE WIRING =====
    App --> AppStore
    App --> FeaturesStore
    App --> ModalStore
    App --> FieldStore
    App --> ToastStore
    App --> ConfirmStore
    App --> ContextMenuStore
    App --> SelectionStore
    App --> RotationStore

    App --> PhaseBar
    App --> FeatureModal
    App --> InfoPanel
    App --> ProfileMenu
    App --> TileControl
    App --> SettingsModal
    App --> AdminDashboard
    App --> ContextMenuCmp
    App --> EditSaveButton
    App --> FieldPanel
    App --> ToastContainer
    App --> ConfirmDialogCmp
    App --> WilayaDetailPage

    FeatureModal --> AreaTypeSelector
    FeatureModal --> BuildingTypeSelector
    FeatureModal --> UseFeatureValidation
    FeatureModal --> ModalStore

    FieldPanel --> FieldStore
    FieldPanel --> RoadInspectionForm
    FieldPanel --> EntranceInspectionForm
    FieldPanel --> NamingPanelInspectionForm

    PhaseBar --> AppStore
    PhaseBar --> PhaseNav
    InfoPanel --> FeaturesStore
    AdminDashboard --> AppStore
    AdminDashboard --> StatPill
    AdminDashboard --> DairaList
    AdminDashboard --> CommuneList
    ProfileMenu --> AppStore
    ProfileMenu --> ApiModule

    SettingsModal --> SettingsGeneral
    SettingsModal --> SettingsAccount
    SettingsModal --> SettingsUsers
    SettingsModal --> SettingsFeatures
    SettingsModal --> SettingsAbout

    SettingsGeneral --> UseTheme

    %% ===== MAP MODULE WIRING =====
    MapInit --> MapContext
    MapInit --> DrawControl
    MapInit --> Labels
    MapInit --> Geometry
    MapInit --> DrawEvents
    MapInit --> DrawHandlers

    DrawEvents --> DrawSave
    DrawEvents --> EditMode
    DrawEvents --> Snapping

    DrawSave --> FeatureDataModule
    DrawSave --> FeaturePersistence
    DrawSave --> LayerStore
    DrawSave --> ModalStore

    EditMode --> MapContext
    EditMode --> LayerStore
    EditMode --> ApiModule

    Snapping --> SnapSearch
    Snapping --> SnapGeometry
    Snapping --> SnapStore

    FeaturePersistence --> ApiModule
    FeatureLoader --> ApiModule
    FeatureLoader --> FeaturesStore
    FeatureLoader --> LayerStore

    PhaseNav --> AppStore
    PhaseNav --> Labels
    PhaseNav --> DrawControl
    PhaseNav --> UseFeatureValidation

    HouseNumbering --> LayerStore
    HouseNumbering --> ApiModule

    Undo --> LayerStore
    Undo --> ApiModule
    Undo --> UndoStore

    ContextMenu --> EditMode
    ContextMenu --> ApiModule
    ContextMenu --> LayerStore
    ContextMenu --> UndoStore
    ContextMenu --> RoadDirections

    RoadDirections --> RoadGraph
    RoadDirections --> RoadOrient
    RoadDirections --> MapContext

    NamingPanels --> LayerStore
    NamingPanels --> MapContext

    %% ===== ARCHITECTURE LAYERS =====
    class I18n {
        +t(key)
        +locale
    }

    class Config {
        <<static>>
        Phase definitions
        API endpoints
        Colors
    }

    ApiModule --> I18n
    ToastStore --> I18n
```
