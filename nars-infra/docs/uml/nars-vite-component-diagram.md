# NARS-Vite Frontend - Component & Architecture Diagram

```mermaid
classDiagram
    %% ===== TYPES =====
    class FeatureData {
        +string type
        +string label
        +string decisionNumber
        +string decisionDate
        +LatLng[] coordinates
        +number lat
        +number lng
        +number radius
        +string areaTypeKey
        +string districtTypeKey
        +string roadTypeKey
        +string roadDbId
        +string side
        +number entranceNumber
        +string sectorKey
        +string buildingTypeKey
        +string geometry
    }

    class LayerEntry {
        +string id
        +string dbId
        +FeatureData data
        +string type
    }

    class Phase {
        +string key
        +string drawType
        +string geometryType
        +string color
    }

    class UserInfo {
        +string id
        +string username
        +string name
        +string email
        +string role
        +CommuneInfo commune
        +DairaInfo? daira
        +WilayaInfo? wilaya
    }

    class NarsError {
        +string code
        +string context
        +Date timestamp
        +Error? cause
    }

    FeatureData o-- LayerEntry

    %% ===== PINIA STORES =====
    class AppStore {
        State
        +number currentPhase
        +FeatureCounts counts
        +UserInfo? user
        +string municipalityName
        +bool isLoading
        +bool loadError
        Actions
        +setUser(UserInfo)
        +setLoading(bool)
        +updateCounts()
        +reset()
    }

    class ModalStore {
        State
        +bool visible
        +number phaseIndex
        +bool isEdit
        +string? editDbId
        +string label
        +string? areaTypeKey
        +string? roadTypeKey
        +string? entranceTypeKey
        +RoadOption[] roadOptions
        Actions
        +openCreate(phaseIndex)
        +openEdit(dbId)
        +close()
        +resetFields()
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
        Actions
        +addFeature(LayerEntry)
        +removeFeature(dbId)
        +updateFeature(dbId, data)
        +clearLayer(phase)
        +getFeature(dbId)
    }

    %% ===== SERVICES =====
    class ApiModule {
        +apiFetch(path, options)
        -applyCSRF(options)
        -retryWithBackoff(fn)
        -classifyError(response)
    }

    class ValidationModule {
        +validateRoad(coordinates)
        +validateDistrict(coordinates, type)
        +checkDistrictCoverage()
        +checkMainUrbanExists()
        +getRoadSide(roadId, lat, lng)
    }

    class ErrorModule {
        +createNetworkError()
        +createValidationError()
        +createAuthError()
        +withRetry(fn, maxRetries)
        +logError(error)
    }

    class MapContext {
        +Map map
        +MaplibreGeoman geoman
        +Record~string, Source~ sources
        +Record~string, GeoJSON~ cachedGeoJSON
        +StyleDefinition[] styles
    }

    %% ===== MAP MODULES =====
    class MapInit {
        +initMap()
        +setBaseLayer(key)
    }

    class DrawEvents {
        +watchDrawType()
        +handleRightClick()
        +handleLeftClick()
    }

    class DrawComplete {
        +completeDrawingWithGeometry(geometry)
        -openModal()
        -saveFeature(data)
    }

    class GeomanEvents {
        +handleGmCreate()
        +handleGmEditEnd()
        +handleGmRemove()
    }

    class EditMode {
        +enterEditMode(feature)
        +commitEdit()
        +cancelEdit()
    }

    class Snapping {
        +updateSnapTargets()
        +getNearestSnap(point)
        +freezeSnap()
    }

    class PhaseNav {
        +navigatePhase(direction)
        +goToPhase(target)
        +setPhase(index)
    }

    class HouseNumbering {
        +assignNumbers(entrances, road)
        +projectEntrancesOntoRoad()
    }

    class Undo {
        +recordDeletion(feature)
        +restoreLastDeleted()
    }

    MapInit --> MapContext
    DrawEvents --> MapContext
    DrawComplete --> MapContext
    DrawComplete --> ModalStore
    DrawComplete --> LayerStore
    GeomanEvents --> MapContext
    GeomanEvents --> DrawComplete
    EditMode --> MapContext
    EditMode --> LayerStore
    Snapping --> MapContext
    PhaseNav --> AppStore
    PhaseNav --> ValidationModule
    HouseNumbering --> LayerStore
    HouseNumbering --> ValidationModule
    Undo --> LayerStore
    Undo --> ApiModule

    MapInit --> ApiModule
    DrawComplete --> ApiModule
    EditMode --> ApiModule

    %% ===== COMPOSABLES =====
    class UseTheme {
        +theme
        +setTheme(value)
    }

    class UseApiFetch {
        +useApiFetch(path, options)
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

    class AdminDashboard {
        Role-based monitoring
    }

    class AreaTypeSelector {
        Central/Secondary urban
    }

    class RoadAssignmentSelector {
        Pick road for entrance
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

    App --> ProfileMenu
    App --> FeatureModal
    App --> PhaseBar
    App --> InfoPanel
    App --> TileControl
    App --> SettingsModal
    App --> AdminDashboard

    FeatureModal --> AreaTypeSelector
    FeatureModal --> RoadAssignmentSelector
    FeatureModal --> BuildingTypeSelector

    SettingsModal --> SettingsGeneral
    SettingsModal --> SettingsAccount
    SettingsModal --> SettingsUsers
    SettingsModal --> SettingsFeatures
    SettingsModal --> SettingsAbout

    AdminDashboard --> StatPill
    AdminDashboard --> DairaList
    AdminDashboard --> CommuneList

    %% ===== STORE RELATIONSHIPS =====
    App --> AppStore
    App --> ModalStore
    App --> LayerStore
    FeatureModal --> ModalStore
    PhaseBar --> AppStore
    InfoPanel --> AppStore
    AdminDashboard --> AppStore
    ProfileMenu --> AppStore
    ProfileMenu --> ApiModule

    %% ===== LAYERED ARCHITECTURE =====
    class I18n {
        +t(key)
        +locale
    }

    class Toast {
        +success(msg)
        +error(msg)
        +confirm(msg)
    }

    class Config {
        <<static>>
        Phase definitions
        API endpoints
        Colors
    }

    ApiModule --> ErrorModule
    ApiModule --> Toast
    ValidationModule --> ApiModule
    Toast --> I18n
```
