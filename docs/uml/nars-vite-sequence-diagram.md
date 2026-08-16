# NARS-Vite Frontend - Sequence Diagrams

## 1. Application Startup & Auth Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant main.ts
    participant Browser
    participant API (/api/current_user)
    participant App.vue
    participant AppStore
    participant MapInit

    User->>Browser: Navigate to URL
    Browser->>main.ts: Load
    main.ts->>main.ts: Apply saved theme (prevent flash)

    main.ts->>API: GET /api/current_user (credentials: include)
    alt 200 OK
        API-->>main.ts: UserInfo
        main.ts->>AppStore: setUser(UserInfo)
    else 401 Unauthorized
        main.ts->>API: POST /api/refresh
        alt 200 OK (refresh succeeds)
            API-->>main.ts: New JWT + UserInfo
            main.ts->>AppStore: setUser(UserInfo)
        else 401 (refresh expired)
            API-->>main.ts: 401
            main.ts->>Browser: Redirect to /login
            Note over Browser: End of flow
        end
    end

    main.ts->>main.ts: Create Vue app (Pinia + i18n)
    main.ts->>main.ts: Register v-click-outside directive
    main.ts->>Browser: mount(App)
    Browser->>App.vue: Render

    alt User is commune_user
        App.vue->>App.vue: Render map UI
        main.ts->>API: GET /api/features
        API-->>main.ts: Features list
        main.ts->>AppStore: updateCounts()

        main.ts->>MapInit: initMap()
        MapInit->>MapInit: Create MapLibre (center Algeria, zoom 5)
        MapInit->>MapInit: Initialize Geoman
        MapInit->>MapInit: Create GeoJSON sources
        MapInit->>MapInit: Add render layers
        MapInit->>MapInit: Register draw events, geoman events

        main.ts->>main.ts: Load features into layerStore
        main.ts->>main.ts: Restore phase from localStorage
        main.ts->>main.ts: Sync counts, refresh visibility
    else Admin user
        App.vue->>App.vue: Render AdminDashboard
        Note over App.vue,API: No map initialization
    end
```

## 2. Drawing & Feature Creation Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant PhaseBar
    participant AppStore
    participant DrawEvents
    participant Geoman
    participant DrawComplete
    participant ModalStore
    participant FeatureModal
    participant Validation
    participant ApiModule
    participant LayerStore
    participant MapContext

    User->>PhaseBar: Click phase button
    PhaseBar->>AppStore: Get current phase
    AppStore-->>PhaseBar: phaseIndex
    PhaseBar->>PhaseBar: Trigger watchDrawType
    PhaseBar->>Geoman: Enable draw mode (drawType)
    Geoman-->>User: Crosshair cursor + snap guides

    User->>Geoman: Draw shape on map
    Geoman->>Geoman: Capture vertices

    alt Right-click (during draw)
        Geoman->>DrawEvents: Remove last vertex
    else Finish drawing
        Geoman->>DrawEvents: gm:create event
        DrawEvents->>DrawEvents: Normalize geometry
        Note over DrawEvents: Circle->Point+radius<br/>MultiPolygon->Polygon
        DrawEvents->>DrawComplete: completeDrawingWithGeometry(geometry)

        DrawComplete->>ModalStore: openCreate(phaseIndex, geometry)
        ModalStore->>FeatureModal: Show modal with pre-filled fields
        FeatureModal-->>User: Display form

        User->>FeatureModal: Fill fields
        User->>FeatureModal: Click Save

        FeatureModal->>FeatureModal: Validate fields
        alt Invalid
            FeatureModal->>FeatureModal: Show errors
        else Valid
            FeatureModal->>Validation: Validate shape
            Validation->>ApiModule: POST /api/validate/...
            ApiModule->>ApiModule: Server validation
            ApiModule-->>Validation: Valid

            FeatureModal->>ModalStore: resolve({success, data})
            ModalStore->>FeatureModal: Hide modal
            ModalStore-->>DrawComplete: Modal result

            DrawComplete->>ApiModule: POST /api/features FeatureSaveRequest
            ApiModule->>ApiModule: Apply CSRF, timeout, retry
            ApiModule-->>DrawComplete: 200 OK {id}

            DrawComplete->>LayerStore: addFeature(LayerEntry)
            LayerStore->>MapContext: Update GeoJSON source
            MapContext-->>User: Feature rendered on map
            DrawComplete->>AppStore: updateCounts()
        end
    end
```

## 3. Feature Edit Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant MapContext
    participant ContextMenu
    participant EditMode
    participant Geoman
    participant Snapping
    participant LayerStore
    participant ApiModule

    User->>MapContext: Click feature on map
    MapContext->>MapContext: Select feature
    MapContext->>MapContext: Highlight (yellow dashed)

    User->>ContextMenu: Right-click feature
    ContextMenu->>ContextMenu: Show menu [Edit, Delete]

    alt Edit selected
        ContextMenu->>EditMode: enterEditMode(feature)
        EditMode->>LayerStore: getFeature(dbId)
        LayerStore-->>EditMode: FeatureData

        EditMode->>Geoman: Import feature as editable
        Geoman-->>EditMode: Vertex handles shown
        EditMode->>Snapping: Enable vertex snapping

        User->>Geoman: Drag vertex
        Geoman->>EditMode: gm:editend
        EditMode->>EditMode: Update live geometry
        EditMode->>Snapping: Apply snap if near target

        User->>Geoman: Right-click (commit)
        Geoman->>EditMode: Commit edit
        EditMode->>LayerStore: updateFeature(dbId, newGeometry)
        EditMode->>ApiModule: PUT /api/features/{id}
        ApiModule-->>EditMode: 200 OK
        EditMode->>Geoman: Disable edit mode
        EditMode->>MapContext: Update highlight
        MapContext-->>User: Updated feature visible

    else Delete selected
        ContextMenu->>ContextMenu: Confirm deletion
        User->>ContextMenu: Confirm
        ContextMenu->>ApiModule: DELETE /api/features/{id}
        ApiModule-->>ContextMenu: 200 OK
        ContextMenu->>Undo: recordDeletion(feature)
        ContextMenu->>LayerStore: removeFeature(dbId)
        LayerStore->>MapContext: Remove from GeoJSON
        MapContext-->>User: Feature removed
    end
```

## 4. Phase Navigation Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant PhaseBar
    participant PhaseNav
    participant Validation
    participant ApiModule
    participant AppStore
    participant LocalStorage
    participant MapInit

    User->>PhaseBar: Click "next" phase
    PhaseBar->>PhaseNav: navigatePhase(+1)

    PhaseNav->>PhaseNav: Check prerequisites

    alt Advancing from areas (0->1)
        PhaseNav->>Validation: checkMainUrbanExists()
        Validation->>ApiModule: GET /api/validate/area/main-urban-exists
        ApiModule-->>Validation: {hasCentralUrban: bool}
        alt No central urban area
            Validation-->>PhaseNav: false
            PhaseNav-->>User: Show warning toast
        end
    else Advancing from districts (1->2)
        PhaseNav->>Validation: checkDistrictCoverage()
        Validation->>ApiModule: GET /api/validate/districts/coverage
        ApiModule-->>Validation: {covered: bool}
        alt Incomplete coverage
            Validation-->>PhaseNav: false
            PhaseNav-->>User: Show warning toast
        end
    else Advancing from roads (3->4)
        PhaseNav->>AppStore: Check counts.roads
        alt No roads created
            PhaseNav-->>User: Show warning toast
        end
    else Advancing from entrances (4->5)
        PhaseNav->>AppStore: Check counts.entrances
        alt No entrances created
            PhaseNav-->>User: Show warning toast
        end
    else Going backward
        Note over PhaseNav: No validation, direct navigation
    end

    PhaseNav->>AppStore: setPhase(targetIndex)
    AppStore->>LocalStorage: Save phase per commune
    PhaseNav->>MapInit: Build draw control for new phase
    PhaseNav->>MapInit: Refresh layer visibility
    MapInit-->>User: UI updated for new phase
```

## 5. House Number Assignment Flow

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant User (draws entrance)
    participant Geoman
    participant DrawComplete
    participant Validation
    participant ApiModule
    participant HouseNumbering
    participant LayerStore

    User->>Geoman: Draw entrance marker near road
    Geoman->>DrawComplete: gm:create event

    DrawComplete->>Validation: getRoadSide(roadId, lat, lng)
    Validation->>ApiModule: POST /api/road-side {roadId, lat, lng}
    ApiModule-->>Validation: {side: 'left'/'right', entranceNumber}
    Validation-->>DrawComplete: {side, number}

    DrawComplete->>DrawComplete: Open modal with auto-filled side + number
    DrawComplete->>User: Show modal

    User->>DrawComplete: Confirm save
    DrawComplete->>ApiModule: POST /api/features {type: 'house_entrance', ...}
    ApiModule-->>DrawComplete: 200 OK {id}
    DrawComplete->>LayerStore: addFeature(entrance)

    alt Scattered area needs refresh
        Note over ApiModule,LayerStore: Backend triggers async refresh
    end

    Note over User: For bulk numbering:
    User->>HouseNumbering: Trigger bulk assignment
    HouseNumbering->>LayerStore: Get unassigned entrances
    HouseNumbering->>Validation: Project onto reference road
    HouseNumbering->>HouseNumbering: Sort by arc-length
    HouseNumbering->>HouseNumbering: Assign odd/even by side
    HouseNumbering->>LayerStore: Update each entrance
    HouseNumbering->>ApiModule: PUT /api/features/{id} for each
```
