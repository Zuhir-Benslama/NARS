# NARS-Vite Frontend - Sequence Diagrams

## 1. Application Startup & Auth Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant main.ts
    participant Browser
    participant App.vue
    participant AppStore
    participant ApiModule
    participant MapInit
    participant FeatureLoader
    participant Geometry

    User->>Browser: Navigate to URL
    Browser->>main.ts: Load
    main.ts->>main.ts: Apply saved theme (prevent flash)

    main.ts->>main.ts: checkAuth() (inline function in main.ts)
    main.ts->>ApiModule: GET /api/current_user (credentials: include)

    alt 200 OK (access token valid)
        ApiModule-->>main.ts: UserInfo
    else 401 (token expired or missing)
        ApiModule-->>main.ts: 401
        main.ts->>ApiModule: POST /api/refresh (single-flight)
        Note over ApiModule: refreshSession() deduplicates concurrent calls

        alt Refresh succeeds (200)
            ApiModule-->>main.ts: new JWT in cookies
            main.ts->>ApiModule: GET /api/current_user (retry with new token)
            ApiModule-->>main.ts: UserInfo
        else Refresh fails (401)
            ApiModule-->>main.ts: 401
            main.ts-->>User: Redirect to /login
            Note over User: End of flow
        end
    end

    main.ts->>AppStore: setUser(user) on successful auth

    main.ts->>main.ts: Create Vue app (Pinia + i18n)
    main.ts->>main.ts: Register v-click-outside directive
    main.ts->>Browser: mount(App)
    Browser->>App.vue: Render

    alt User is commune_user
        App.vue->>App.vue: Render map UI
        main.ts->>FeatureLoader: loadUserAndCommune()
        FeatureLoader->>ApiModule: GET /api/current_user
        ApiModule-->>FeatureLoader: UserInfo
        FeatureLoader->>AppStore: setUser(UserInfo)

        main.ts->>MapInit: initMap()
        MapInit->>MapInit: Create MapLibre (center Algeria, zoom 5)
        MapInit->>MapInit: Initialize Geoman
        MapInit->>MapInit: Create GeoJSON sources + render layers
        MapInit->>MapInit: Register draw events, geoman events

        main.ts->>FeatureLoader: loadFromDatabase()
        FeatureLoader->>ApiModule: GET /api/features
        ApiModule-->>FeatureLoader: FeatureResult[]
        FeatureLoader->>FeatureLoader: Build GeoJSON + populate stores
        FeatureLoader->>FeatureLoader: Restore phase from localStorage
        FeatureLoader->>FeatureLoader: refreshLayerVisibility()

        main.ts->>Geometry: displayCommuneBoundary(communeId)
        Geometry->>ApiModule: GET /api/commune/{id}/boundary
        ApiModule-->>Geometry: Boundary GeoJSON (cached for style switches)
    else Admin/other role
        App.vue->>App.vue: Render AdminDashboard (admin) or FieldPanel (field_worker)
        Note over App.vue: No map initialization
    end
```

## 2. Drawing & Feature Creation Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant PhaseBar
    participant PhaseNav
    participant DrawControl
    participant DrawHandlers
    participant Geoman
    participant DrawSave
    participant ModalStore
    participant FeatureModal
    participant UseFeatureValidation
    participant ApiModule
    participant FeaturesStore
    participant LayerStore

    User->>PhaseBar: Click phase button
    PhaseBar->>PhaseNav: goToPhase(index)
    PhaseNav->>DrawControl: buildDrawControl(phase)
    DrawControl->>Geoman: Enable draw mode (drawType)
    Geoman-->>User: Crosshair cursor + snap guides

    User->>Geoman: Draw shape on map
    Geoman->>DrawHandlers: Capture vertices + snapping

    alt Right-click (during draw)
        Geoman->>DrawHandlers: Remove last vertex
    else Finish drawing
        Geoman->>DrawSave: gm:create event
        DrawSave->>DrawSave: normalizeGeometry(geometry, drawType)
        Note over DrawSave: Circle->Point+radius<br/>MultiPolygon->Polygon

        DrawSave->>ModalStore: openCreate(phaseIndex, geometry)
        ModalStore->>FeatureModal: Show modal with pre-filled fields
        FeatureModal-->>User: Display form

        User->>FeatureModal: Fill fields
        User->>FeatureModal: Click Save

        FeatureModal->>UseFeatureValidation: validate()
        Note over UseFeatureValidation: Client-side only — no API call.\nPer-type geometry/structure checks + modal errors.

        alt Invalid
            UseFeatureValidation-->>FeatureModal: Show errors
        else Valid
            FeatureModal->>ModalStore: close({success: true, data})
            ModalStore-->>DrawSave: Modal result

            DrawSave->>DrawSave: buildFeatureData(geometry, phase, modalResult)
            DrawSave->>DrawSave: toApiSaveShape(featureData)
            DrawSave->>ApiModule: POST /api/features FeatureSaveRequest
            ApiModule->>ApiModule: Apply CSRF, timeout, retry
            ApiModule-->>DrawSave: 200 OK {id}

            DrawSave->>FeaturesStore: add(MaplibreFeature)
            DrawSave->>FeaturesStore: updateSource()
            DrawSave->>LayerStore: addFeature(layer, LayerEntry)
            FeaturesStore-->>User: Feature rendered on map
        end
    end
```

## 3. Feature Edit Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant ContextMenu
    participant EditMode
    participant Geoman
    participant Snapping
    participant LayerStore
    participant FeaturesStore
    participant ApiModule
    participant Undo

    User->>ContextMenu: Right-click feature on map
    ContextMenu->>ContextMenu: showContextMenu(x, y, dbId, phaseKey)

    alt Edit selected
        ContextMenu->>EditMode: enableEditMode(dbId)
        EditMode->>LayerStore: getFeature(dbId)
        LayerStore-->>EditMode: LayerEntry

        EditMode->>Geoman: Import feature as editable
        Geoman-->>EditMode: Vertex handles shown
        EditMode->>Snapping: installSnapInterceptors()

        User->>Geoman: Drag vertex
        Geoman->>EditMode: gm:editend
        EditMode->>EditMode: Update live geometry

        User->>Geoman: Right-click (commit)
        Geoman->>EditMode: commitEditMode()
        EditMode->>LayerStore: updateFeature(layer, dbId, data)
        EditMode->>ApiModule: PUT /api/features/{id}
        ApiModule-->>EditMode: 200 OK
        EditMode->>Geoman: Disable edit mode
        EditMode-->>User: Updated feature visible

    else Delete selected
        ContextMenu->>ContextMenu: Confirm deletion
        User->>ContextMenu: Confirm
        ContextMenu->>ApiModule: DELETE /api/features/{id}
        ApiModule-->>ContextMenu: 200 OK
        ContextMenu->>Undo: recordDelete(entry, phaseKey)
        ContextMenu->>FeaturesStore: remove(id)
        FeaturesStore->>FeaturesStore: updateSource()
        ContextMenu->>LayerStore: removeFeature(layer, dbId)
        FeaturesStore-->>User: Feature removed
    end
```

## 4. Phase Navigation Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant PhaseBar
    participant PhaseNav
    participant LayerStore
    participant ValidationLib
    participant ApiModule
    participant AppStore
    participant LocalStorage
    participant DrawControl
    participant Labels

    User->>PhaseBar: Click "next" phase
    PhaseBar->>PhaseNav: navigatePhase(+1)

    PhaseNav->>PhaseNav: Check prerequisites

    alt Advancing from areas (0->1)
        PhaseNav->>LayerStore: Check $state.areas.length
        alt No urban area created
            PhaseNav-->>User: Show warning toast
        end
    else Advancing from districts (1->2)
        PhaseNav->>ValidationLib: checkDistrictCoverage()
        ValidationLib->>ApiModule: GET /api/validate/districts/coverage
        ApiModule-->>ValidationLib: {covered: bool}
        alt Incomplete coverage
            ValidationLib-->>PhaseNav: false
            PhaseNav-->>User: Show warning toast
        end
    else Advancing from roads (3->4)
        PhaseNav->>LayerStore: Check $state.roads.length
        alt No roads created
            PhaseNav-->>User: Show warning toast
        else Roads exist
            PhaseNav->>PhaseNav: computeAndApplyRoadDirections()
        end
    else Advancing from entrances (4->5)
        PhaseNav->>LayerStore: Check $state.houseEntrances.length
        alt No entrances created
            PhaseNav-->>User: Show warning toast
        end
    else Going backward
        Note over PhaseNav: No validation, direct navigation
    end

    PhaseNav->>AppStore: setPhase(targetIndex)
    AppStore->>LocalStorage: Save phase per commune
    PhaseNav->>DrawControl: buildDrawControl(newPhase)
    PhaseNav->>Labels: refreshLayerVisibility()
    Labels-->>User: UI updated for new phase
```

## 5. House Number Assignment Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Geoman
    participant DrawSave
    participant DrawModal
    participant ValidationLib
    participant ApiModule
    participant HouseNumbering
    participant LayerStore
    participant Turf

    User->>Geoman: Draw entrance marker near road
    Geoman->>DrawSave: gm:create event

    DrawSave->>DrawModal: Prepare modal for house_entrance
    DrawModal->>ValidationLib: getRoadSide(roadDbId, lat, lng)
    ValidationLib->>ApiModule: POST /api/road-side {roadId, lat, lng}
    ApiModule-->>ValidationLib: {side: 'left'/'right', suggestedNumber}
    ValidationLib-->>DrawModal: {side, number}
    DrawModal-->>DrawSave: Modal result

    DrawSave->>DrawSave: Open modal with auto-filled side + number
    DrawSave->>User: Show modal

    User->>DrawSave: Confirm save
    DrawSave->>ApiModule: POST /api/features {type: 'house_entrance', ...}
    ApiModule-->>DrawSave: 200 OK {id}
    DrawSave->>LayerStore: addFeature(layer, entrance)

    Note over User: For bulk numbering:
    User->>HouseNumbering: Trigger setHouseNumbers()
    HouseNumbering->>LayerStore: Get entrances for reference road
    HouseNumbering->>Turf: nearestPointOnLine (project onto road)
    Turf-->>HouseNumbering: arc-length position
    HouseNumbering->>HouseNumbering: Sort by arc-length, assign odd/even
    HouseNumbering->>ApiModule: POST /api/features/number-entrances {roadId, entranceIds}
    ApiModule-->>HouseNumbering: Ordered entrance numbers
    HouseNumbering->>LayerStore: Update each entrance
```

## 6. Session Refresh Flow (Single-Flight)

```mermaid
sequenceDiagram
    autonumber
    participant Tab1 as Tab 1
    participant Tab2 as Tab 2
    participant refreshPromise as refreshSession()
    participant ApiModule
    participant Server

    Note over Tab1,Tab2: Both tabs detect 401 simultaneously

    Tab1->>refreshPromise: apiFetch() fails -> POST /api/refresh
    Tab2->>refreshPromise: apiFetch() fails -> POST /api/refresh

    Note over refreshPromise: Single-flight dedup:<br/>first caller starts request,<br/>second caller waits on same Promise

    refreshPromise->>ApiModule: POST /api/refresh (only once)
    ApiModule->>Server: Cookie: refresh_token=...
    Server->>Server: SHA-256 hash + validate
    Server->>Server: Revoke old token + issue new
    Server-->>ApiModule: 200 OK + Set-Cookie headers
    ApiModule-->>refreshPromise: Success

    refreshPromise-->>Tab1: Resolve (new token in cookies)
    refreshPromise-->>Tab2: Resolve (same result)

    Tab1->>ApiModule: Retry original request with new cookie
    Tab2->>ApiModule: Retry original request with new cookie
    ApiModule-->>Tab1: 200 OK (data)
    ApiModule-->>Tab2: 200 OK (data)
```
