// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────
// Initialises the map, wires draw events, handles phase navigation and loading.
// All heavy logic lives in the map-* sub-modules.

import { PHASES, API_LAYER_TO_PHASE, DISTRICT_TYPES } from '../phases'
import { apiFetch }                   from '../api'
import { store, featureLayers, openModal, syncCounts } from '../store'
import { validateRoad, validateDistrict, checkDistrictCoverage } from '../validation'
import type { FeatureData, LayerEntry, DbFeature }    from '../types'

import { ctx, POLYLINE_WEIGHT }       from './state'
import { areaStyle, polygonStyles, createEntranceIcon, applyStyle, buildPopup } from './styles'
import { addPolylineEndpoints, createPermanentLabel, createAreaPerimeterLabel, createPolygonEdgeLabel, refreshAllEdgeLabels, refreshLayerVisibility } from './labels'
import { pointInMunicipalLimit, pointInScatteredArea, polylineMidpoint, displayCommuneBoundary, renderScatteredAreas, refreshScatteredAreas } from './geometry'
import { enableSnapping, disableSnapping, hookEditHandles, hookAllEditMarkers, editModeActive, installSnapInterceptors } from './snapping'
import { bindContextMenu, showMapContextMenu } from './context-menu'
import { buildFeatureData, saveToDatabase, prepareModalExtras } from './features'
import { computeAndApplyRoadDirections } from './road-directions'

// Re-export public API consumed by Vue components and main.ts
export { displayCommuneBoundary }                           from './geometry'
export { bindContextMenu }                                  from './context-menu'
export { fetchRoadSide, computeBisNumber }                  from './features'
export { createEntranceIcon, areaStyle }                    from './styles'
export { createPolygonEdgeLabel, createAreaPerimeterLabel } from './labels'

declare const L: typeof import('leaflet')

// Returns the label to display for a district. For Trade Activity Zones and Industrial Zones,
// uses the type name when no custom label is provided.
function getDistrictLabel(districtTypeKey: string, customLabel: string): string {
    if (customLabel) return customLabel
    if (districtTypeKey === 'trad_activities_zone' || districtTypeKey === 'industry_zone') {
        const dtype = DISTRICT_TYPES.find(d => d.key === districtTypeKey)
        return dtype?.label ?? ''
    }
    return customLabel
}

// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────

export function initMap(): void {
    ctx.map = L.map('map', { zoomControl: false }).setView([28.0, 2.5], 5)

    // Disable Geoman's built-in snap indicator — NARS uses its own custom
    // snapping logic (snapping.ts). Leaving this on causes Geoman's dot to
    // jump randomly to nearby features during draw mode.
    ;(ctx.map as any).pm?.setGlobalOptions?.({ snappable: false })

    const satellite = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', { attribution: 'Tiles © Esri' })
    const street    = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',  { attribution: '© OpenStreetMap contributors' })
    const carto     = L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    const dark      = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',  { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    satellite.addTo(ctx.map)
    L.control.layers({ Satellite: satellite, Street: street, Light: carto, Dark: dark }, undefined, { position: 'bottomleft' }).addTo(ctx.map)

    ctx.drawnItems            = new L.FeatureGroup().addTo(ctx.map)
    ctx.lineEndpointLayer     = L.layerGroup().addTo(ctx.map)
    ctx.scatteredLayer        = L.layerGroup().addTo(ctx.map)
    ctx.perimeterLabelLayer   = L.layerGroup().addTo(ctx.map)
    ctx.polygonEdgeLabelLayer = L.layerGroup().addTo(ctx.map)
    ctx.boundariesLayer       = null

    ctx.map.on('zoomend', refreshAllEdgeLabels)
    installSnapInterceptors()

    buildDrawControl(PHASES[0])
    registerDrawEvents()
}

// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────

function buildDrawControl(phase: typeof PHASES[number]): void {
    // Remove existing Geoman controls
    ctx.map.pm.removeControls()

    // Configure Geoman drawing styles for the current phase via setGlobalOptions.

    if (phase.drawType === 'polygon') {
        ctx.map.pm.setGlobalOptions({
            pathOptions: {
                color:       phase.color,
                weight:      2.5,
                fillOpacity: phase.key === 'areas' ? 0 : 0.15,
                dashArray:   phase.key === 'areas' ? '10, 6' : undefined,
            },
            snappable: false,
        } as any)
    } else if (phase.drawType === 'polyline') {
        ctx.map.pm.setGlobalOptions({
            templineStyle: { color: phase.color, weight: POLYLINE_WEIGHT },
            hintlineStyle: { color: phase.color, weight: POLYLINE_WEIGHT },
            snappable: false,
        } as any)
    } else if (phase.drawType === 'circle') {
        ctx.map.pm.setGlobalOptions({
            pathOptions: { color: '#e74c3c', weight: 2, fillColor: '#e74c3c', fillOpacity: 0.15 },
        } as any)
    } else if (phase.drawType === 'marker') {
        const icon = createEntranceIcon('?', phase.color)
        ctx.map.pm.setGlobalOptions({ markerStyle: { icon } } as any)
    }

    // No toolbar rendered. Enable draw mode immediately on phase entry so the
    // cursor is already in draw mode — the first click places a vertex/marker,
    // not a wasted "activate draw" click.
    // cityCenter (circle) is excluded here — cityCenterYes() gates it via the
    // dialog so the OK-button click isn't consumed as an accidental placement.
    if (phase.drawType === 'polygon')       setTimeout(() => (ctx.map as any).pm.enableDraw('Polygon', { snappable: false }), 0)
    else if (phase.drawType === 'polyline') setTimeout(() => (ctx.map as any).pm.enableDraw('Line', { snappable: false }),    0)
    else if (phase.drawType === 'marker')   setTimeout(() => (ctx.map as any).pm.enableDraw('Marker', { snappable: false }),  0)
    // circle (cityCenter) is handled by cityCenterYes()

    // Stamp pmIgnore so Geoman's global edit mode only touches the current phase
    updateLayerEditability(phase.key)
}

// ─── LAYER EDITABILITY ────────────────────────────────────────────────────────
// Called every time the phase changes (via buildDrawControl).
// Sets pmIgnore:true on every non-current-phase feature so Geoman's global
// edit mode never picks them up, regardless of which LayerGroup they live in.

function updateLayerEditability(currentPhaseKey: string): void {
    for (const [key, entries] of Object.entries(featureLayers)) {
        const editable = key === currentPhaseKey
        for (const { layer } of entries as LayerEntry[]) {
            // Set at the Leaflet layer options level — this is what Geoman's
            // global edit mode checks when deciding which layers to make editable.
            ;(layer as any).options.pmIgnore = !editable
            // Also set at the PM handler level for belt-and-suspenders.
            ;(layer as any).pm?.setOptions?.({ pmIgnore: !editable })
        }
    }
}

// ─── PLACEMENT VALIDATION ─────────────────────────────────────────────────────

async function validatePlacement(layer: L.Layer, phase: typeof PHASES[number]): Promise<boolean> {
    let checkPoint: L.LatLng
    if (phase.drawType === 'marker' || phase.drawType === 'circle')
                                           checkPoint = (layer as any).getLatLng()
    else if (phase.drawType === 'polyline') checkPoint = polylineMidpoint(layer as L.Polyline)
    else {
        // Use the actual polygon centroid (average of vertices) rather than the
        // bounding-box center — the bbox center can fall outside a concave polygon
        // or outside the urban area even when the polygon is mostly inside.
        const lls = (layer as L.Polygon).getLatLngs()[0] as L.LatLng[]
        const lat = lls.reduce((s, ll) => s + ll.lat, 0) / lls.length
        const lng = lls.reduce((s, ll) => s + ll.lng, 0) / lls.length
        checkPoint = L.latLng(lat, lng)
    }

    if (!pointInMunicipalLimit(checkPoint)) {
        alert(`⛔ This ${phase.label.replace(/s$/, '').toLowerCase()} is outside the municipal boundary.`)
        return false
    }
    // Districts are excluded here — the scattered-area check for them happens
    // after the modal, once we know the district type (industry zones are allowed).
    if (phase.key !== 'publicBuildings' && phase.key !== 'areas' && phase.key !== 'cityCenter' && phase.key !== 'districts') {
        if (pointInScatteredArea(checkPoint)) {
            alert(`⛔ This ${phase.label.replace(/s$/, '').toLowerCase()} cannot be placed in a scattered area.\nOnly public buildings and industry zones are allowed in scattered areas.`)
            return false
        }
    }
    return true
}

// ─── PHASE NAVIGATION ─────────────────────────────────────────────────────────

export async function navigatePhase(direction: number): Promise<void> {
    const target = store.currentPhase + direction
    if (target < 0 || target >= PHASES.length) return

    if (direction > 0) {
        const from = PHASES[store.currentPhase]
        if (from.key === 'areas'        && featureLayers.areas.length === 0)           { alert('Please draw at least one urban area before proceeding.'); return }
        if (from.key === 'districts') {
            const coverage = await checkDistrictCoverage()
            if (!coverage.covered) { alert(`⛔ ${coverage.message}`); return }
        }
        if (from.key === 'roads'          && featureLayers.roads.length === 0)           { alert('Please draw at least one road before proceeding.'); return }
        if (from.key === 'houseEntrances' && featureLayers.houseEntrances.length === 0) { alert('Please place at least one house entrance before proceeding.'); return }

        // Compute road directions when leaving the Roads phase.
        // Done here (not during drawing) so the full network topology is known.
        if (from.key === 'roads') await computeAndApplyRoadDirections()
    }

    setPhase(target)
}

export async function goToPhase(target: number): Promise<void> {
    if (target === store.currentPhase) return
    if (target > store.currentPhase) {
        for (let i = store.currentPhase; i < target; i++) {
            const before = store.currentPhase
            await navigatePhase(1)
            if (store.currentPhase === before) return
        }
    } else {
        setPhase(target)
    }
}

export function setPhase(index: number): void {
    store.currentPhase = index
    buildDrawControl(PHASES[index])
    disableSnapping()
    if (PHASES[index].key === 'cityCenter' && store.cityCenterMode === null)
        store.cityCenterDialogVisible = true
    refreshLayerVisibility()
}

export function cityCenterYes(): void {
    store.cityCenterDialogVisible = false
    // Enable Circle draw mode after dialog closes — deferred so the OK click
    // is not consumed by Geoman as an accidental shape placement.
    setTimeout(() => (ctx.map as any).pm.enableDraw('Circle', { snappable: false }), 0)
}

// ─── DISCARD A NEWLY CREATED LAYER ───────────────────────────────────────────
// Geoman adds the layer to the map the moment pm:create fires, before our
// handler runs. Every early-return path (validation failure, modal cancel,
// save error) must call this so the ghost shape does not linger on screen.

function discardCreatedLayer(layer: L.Layer): void {
    ctx.map.removeLayer(layer)
    // Belt-and-suspenders: also evict from drawnItems in case Geoman added it there
    if (ctx.drawnItems.hasLayer(layer)) ctx.drawnItems.removeLayer(layer)
}

// ─── DRAW EVENTS ──────────────────────────────────────────────────────────────

// Opens the feature info popup on hover instead of click.
// Uses a standard Leaflet popup so the existing buildPopup HTML is reused.
function bindHoverPopup(layer: L.Layer, content: string): void {
    if (layer instanceof L.Marker) {
        const popup = L.popup({ offset: L.point(0, -10), closeButton: false }).setContent(content)
        layer.bindPopup(popup)
        layer.on('mouseover', () => layer.openPopup())
        layer.on('mouseout',  () => layer.closePopup())
    } else {
        const path = layer as L.Path
        const popup = L.popup({ closeButton: false }).setContent(content)
        path.bindPopup(popup)
        layer.on('mouseover', (e: any) => path.openPopup(e.latlng))
        layer.on('mouseout',  () => path.closePopup())
    }
}

function registerDrawEvents(): void {
    // Geoman uses 'pm:drawstart', 'pm:drawend', 'pm:editstart', 'pm:editend', etc.
    
    // Track whether Geoman draw mode is currently active so the left-click
    // handler knows not to re-trigger it while the user is already drawing.
    let drawModeActive = false

    ctx.map.on('pm:drawstart', (e: any) => {
        drawModeActive = true
        const key = PHASES[store.currentPhase]?.key
        if      (key === 'areas')     enableSnapping('districts', undefined, 'areas')
        else if (key === 'districts') enableSnapping('districts', undefined, 'districts')
        else if (key === 'roads')     enableSnapping('roads',     undefined, 'roads')
    })

    ctx.map.on('pm:drawend', () => {
        drawModeActive = false
        if (!editModeActive) disableSnapping()
    })

    // ESC cancels an in-progress draw without creating a feature.
    document.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key === 'Escape' && drawModeActive) {
            ;(ctx.map as any).pm.disableDraw()
            // Re-enable draw after a tick so the user can start a new shape.
            const phase = PHASES[store.currentPhase]
            if      (phase?.drawType === 'polygon')  setTimeout(() => (ctx.map as any).pm.enableDraw('Polygon', { snappable: false }), 50)
            else if (phase?.drawType === 'polyline') setTimeout(() => (ctx.map as any).pm.enableDraw('Line',    { snappable: false }), 50)
            else if (phase?.drawType === 'marker')   setTimeout(() => (ctx.map as any).pm.enableDraw('Marker',  { snappable: false }), 50)
        }
    })

    // Right-click on the map background (not on a feature):
    // • Roads phase  → show "Set Road Directions" only
    // • Other phases → show "Start Drawing" (triggers Geoman draw mode)
    ctx.map.on('contextmenu', (e: any) => {
        e.originalEvent.preventDefault()
        e.originalEvent.stopPropagation()
        // Leaflet always re-fires contextmenu to the map even after a layer
        // handled it. Skip if a feature's contextmenu already ran this tick.
        if ((ctx.map as any)._narsFeatureCtxHandled) {
            ;(ctx.map as any)._narsFeatureCtxHandled = false
            return
        }
        const phase = PHASES[store.currentPhase]
        if (!phase) return
        showMapContextMenu(e.originalEvent.clientX, e.originalEvent.clientY, phase)
    })

    ctx.map.on('pm:editstart', (e: any) => {
        try {
            const key = PHASES[store.currentPhase]?.key
            console.log('pm:editstart - editing phase:', key)

            // Remove non-current-phase layers from drawnItems so Geoman
            // cannot select or edit them
            const parked:  L.Layer[] = []
            const display: L.Layer[] = []

            if (!(ctx as any)._displayLayer) {
                ;(ctx as any)._displayLayer = L.layerGroup().addTo(ctx.map)
            }
            const displayLayer: L.LayerGroup = (ctx as any)._displayLayer

            Object.entries(featureLayers).forEach(([phaseKey, entries]) => {
                if (phaseKey === key) return
                ;(entries as LayerEntry[]).forEach(({ layer }) => {
                    if (!ctx.drawnItems.hasLayer(layer)) return
                    ctx.drawnItems.removeLayer(layer)
                    if (phaseKey === 'areas') {
                        displayLayer.addLayer(layer)
                        display.push(layer)
                    } else {
                        parked.push(layer)
                    }
                })
            })
            ;(ctx as any)._parkedLayers  = parked
            ;(ctx as any)._displayLayers = display

            if      (key === 'districts' || key === 'areas') enableSnapping('districts', undefined, key)
            else if (key === 'roads')                        enableSnapping('roads',     undefined, key)
            hookEditHandles()
        } catch (err) {
            console.error('pm:editstart error:', err)
        }
    })

    // Handle new vertex added (e.g., via midpoint click)
    let editVertexTimeout: ReturnType<typeof setTimeout> | null = null
    ctx.map.on('pm:vertexadded', () => {
        if (editVertexTimeout) clearTimeout(editVertexTimeout)
        editVertexTimeout = setTimeout(() => {
            hookAllEditMarkers()
        }, 150)
    })

    ctx.map.on('pm:editend', () => {
        const parked:  L.Layer[]      = (ctx as any)._parkedLayers  ?? []
        const display: L.Layer[]      = (ctx as any)._displayLayers ?? []
        const displayLayer: L.LayerGroup | undefined = (ctx as any)._displayLayer

        // Move display-only areas back into drawnItems
        display.forEach(layer => {
            displayLayer?.removeLayer(layer)
            ctx.drawnItems.addLayer(layer)
        })
        // Restore all other parked layers
        parked.forEach(layer => ctx.drawnItems.addLayer(layer))

        ;(ctx as any)._parkedLayers  = []
        ;(ctx as any)._displayLayers = []
        disableSnapping()
        // Re-enable draw mode after finishing geometry edit
        const phaseAfterEdit = PHASES[store.currentPhase]
        if      (phaseAfterEdit?.drawType === 'polygon')  setTimeout(() => (ctx.map as any).pm.enableDraw('Polygon', { snappable: false }), 50)
        else if (phaseAfterEdit?.drawType === 'polyline') setTimeout(() => (ctx.map as any).pm.enableDraw('Line', { snappable: false }),    50)
        else if (phaseAfterEdit?.drawType === 'marker')   setTimeout(() => (ctx.map as any).pm.enableDraw('Marker', { snappable: false }),  50)

        // ── Belt-and-suspenders save ─────────────────────────────────────────
        // pm:edit fires per-drag in Geoman 2.x; its async save (setTimeout 0)
        // runs after our synchronous snap correction, so coordinates are correct.
        // This extra save on editend acts as a safety net in case pm:edit missed
        // any changes (e.g. snap edge-cases, version differences).
        const currentKey = PHASES[store.currentPhase]?.key
        if (currentKey) {
            setTimeout(async () => {
                for (const entry of (featureLayers[currentKey] ?? []) as LayerEntry[]) {
                    const dbId = (entry.layer as any)._dbId
                    if (!dbId) continue
                    try {
                        const updatedData = { ...entry.data }
                        if (entry.layer instanceof L.Marker) {
                            const ll = (entry.layer as L.Marker).getLatLng()
                            updatedData.lat = ll.lat; updatedData.lng = ll.lng
                            entry.data.lat  = ll.lat; entry.data.lng  = ll.lng
                        } else if (entry.layer instanceof L.Polygon) {
                            let coords = ((entry.layer as L.Polygon).getLatLngs()[0] as L.LatLng[])
                                .map(ll => ({ lat: ll.lat, lng: ll.lng }))
                            if (coords.length >= 3) {
                                const f = coords[0], l = coords[coords.length - 1]
                                if (f.lat !== l.lat || f.lng !== l.lng)
                                    coords = [...coords, { lat: f.lat, lng: f.lng }]
                            }
                            updatedData.coordinates = coords
                            entry.data.coordinates  = coords
                        } else if (entry.layer instanceof L.Polyline) {
                            const coords = ((entry.layer as L.Polyline).getLatLngs() as L.LatLng[])
                                .map(ll => ({ lat: ll.lat, lng: ll.lng }))
                            updatedData.coordinates = coords
                            entry.data.coordinates  = coords
                        }
                        await apiFetch(`/api/update/${dbId}`, {
                            method:  'PUT',
                            headers: { 'Content-Type': 'application/json' },
                            body:    JSON.stringify({ data: updatedData }),
                        })
                    } catch (err) { console.error('editend save error for', dbId, err) }
                }
            }, 30) // 30 ms: lets any pending snap redraw settle before reading coords
        }

        setTimeout(refreshLayerVisibility, 0)
    })

    // Geoman fires pm:create with { layer, shape } after a shape is completed
    ctx.map.on('pm:create', async (event: any) => {
        const layer = event.layer as L.Layer
        const phase = PHASES[store.currentPhase]

        if (!await validatePlacement(layer, phase)) { discardCreatedLayer(layer); return }

        // For districts, open modal first to get the district type before validation
        let modalResult: any = null
        if (phase.key === 'districts') {
            await prepareModalExtras(phase, layer)
            modalResult = await openModal(store.currentPhase, layer)
            if (!modalResult) { discardCreatedLayer(layer); return }
            
            const districtTypeKey = modalResult.districtTypeKey as string
            const check = await validateDistrict(layer as L.Polygon, districtTypeKey)
            if (!check.valid) { discardCreatedLayer(layer); alert(`⛔ District cannot be saved:\n${check.error}`); return }
        } else {
            if (phase.key === 'roads') {
                const check = await validateRoad(layer as L.Polyline)
                if (!check.valid) { discardCreatedLayer(layer); alert(`⛔ Road cannot be saved:\n${check.error}`); return }
            }
        }

        // For non-districts, open modal after validation
        if (phase.key !== 'districts') {
            await prepareModalExtras(phase, layer)
            modalResult = await openModal(store.currentPhase, layer)
            if (!modalResult) { discardCreatedLayer(layer); return }
        }

        // ── Area count rules ─────────────────────────────────────────────────
        if (phase.key === 'areas') {
            const areaTypeKey = (modalResult as any).areaTypeKey as string
            const mainCount      = featureLayers.areas.filter((e: LayerEntry) => e.data.areaTypeKey === 'central_urban').length
            const secondaryCount = featureLayers.areas.filter((e: LayerEntry) => e.data.areaTypeKey === 'secondary_urban').length

            if (areaTypeKey === 'central_urban' && mainCount >= 1) {
                discardCreatedLayer(layer)
                alert('⛔ A municipality can only have one main urban area.')
                return
            }
            if (areaTypeKey === 'secondary_urban' && secondaryCount >= 10) {
                discardCreatedLayer(layer)
                alert('⛔ A municipality cannot have more than 10 secondary urban areas.')
                return
            }
        }

        // ── City center: one per urban area ─────────────────────────────────
        if (phase.key === 'cityCenter') {
            const markerLL = (layer as L.Circle).getLatLng()

            // Find which urban area polygon the marker falls inside
            const parentArea = featureLayers.areas.find((e: LayerEntry) => {
                if (!(e.layer instanceof L.Polygon)) return false
                const ring = (e.layer.getLatLngs()[0] as L.LatLng[])
                // Ray-cast point-in-polygon
                let inside = false
                const x = markerLL.lat, y = markerLL.lng
                for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
                    const xi = ring[i].lat, yi = ring[i].lng
                    const xj = ring[j].lat, yj = ring[j].lng
                    if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                        inside = !inside
                }
                return inside
            })

            if (!parentArea) {
                discardCreatedLayer(layer)
                alert('⛔ The city center marker must be placed inside an urban area.')
                return
            }

            // Check whether a city center already exists inside the same urban area
            const parentRing = (parentArea.layer as L.Polygon).getLatLngs()[0] as L.LatLng[]
            const duplicate = featureLayers.cityCenter.some((e: LayerEntry) => {
                if (!(e.layer instanceof L.Circle)) return false
                const ll = (e.layer as L.Circle).getLatLng()
                let inside = false
                const x = ll.lat, y = ll.lng
                for (let i = 0, j = parentRing.length - 1; i < parentRing.length; j = i++) {
                    const xi = parentRing[i].lat, yi = parentRing[i].lng
                    const xj = parentRing[j].lat, yj = parentRing[j].lng
                    if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                        inside = !inside
                }
                return inside
            })

            if (duplicate) {
                discardCreatedLayer(layer)
                const areaLabel = parentArea.data.label || 'this urban area'
                alert(`⛔ A city center already exists inside "${areaLabel}". Each urban area can have at most one city center.`)
                return
            }
        }

        // ── District scattered-area check (type-aware) ───────────────────────
        if (phase.key === 'districts') {
            const districtTypeKey = (modalResult as any).districtTypeKey as string
            const dtype = DISTRICT_TYPES.find(d => d.key === districtTypeKey)
            if (!dtype?.allowInScattered) {
                // Recompute centroid here — same logic as validatePlacement
                const lls = (layer as L.Polygon).getLatLngs()[0] as L.LatLng[]
                const lat = lls.reduce((s: number, ll: L.LatLng) => s + ll.lat, 0) / lls.length
                const lng = lls.reduce((s: number, ll: L.LatLng) => s + ll.lng, 0) / lls.length
                if (pointInScatteredArea(L.latLng(lat, lng))) {
                    discardCreatedLayer(layer)
                    alert('⛔ This district type cannot be placed in a scattered area.\nOnly Industry Zones are allowed in scattered areas.')
                    return
                }
            }
        }

        applyStyle(layer, phase, modalResult as unknown as FeatureData)

        const featureData = buildFeatureData(layer, phase, modalResult as unknown as Record<string, unknown>)
        const saveResult  = await saveToDatabase(featureData)
        if (!saveResult.ok) { discardCreatedLayer(layer); alert(`Failed to save feature.\n${saveResult.error ?? 'Please try again.'}`); return }

        ;(layer as any)._dbId = saveResult.data!.id
        ctx.drawnItems.addLayer(layer)
        bindContextMenu(layer, saveResult.data!.id, phase.key)
        // Endpoint arrows for roads are added by computeAndApplyRoadDirections(),
        // not here — direction isn't known until the full network is finalised.
        createPermanentLabel(layer, modalResult.label as string, phase.key)
        if (phase.key === 'areas')     createAreaPerimeterLabel(layer, (modalResult as any).areaTypeKey as string)
        if (phase.key === 'districts') createPolygonEdgeLabel(layer, getDistrictLabel((modalResult as any).districtTypeKey as string, modalResult.label as string), '#f39c12')
        bindHoverPopup(layer, buildPopup(featureData, phase, saveResult.data!.id))

        featureLayers[phase.key].push({ layer, data: featureData })

        if (phase.key === 'cityCenter') {
            const ll = (layer as L.Circle).getLatLng()
            store.cityCenterMode   = 'city_center'
            store.cityCenterLatLng = { lat: ll.lat, lng: ll.lng }
            // Re-enable circle draw so user can place another city center
            setTimeout(() => (ctx.map as any).pm.enableDraw('Circle', { snappable: false }), 0)
        }

        // Re-enable marker draw immediately after placement
        if (phase.drawType === 'marker') {
            setTimeout(() => (ctx.map as any).pm.enableDraw('Marker', { snappable: false }), 0)
        }

        if (phase.key === 'areas') await refreshScatteredAreas()

        // Re-enable draw after modal + save — polygon/polyline only.
        if (phase.drawType === 'polygon')
            setTimeout(() => (ctx.map as any).pm.enableDraw('Polygon', { snappable: false }), 0)
        else if (phase.drawType === 'polyline')
            setTimeout(() => (ctx.map as any).pm.enableDraw('Line',    { snappable: false }), 0)

        syncCounts()
    })

    // Geoman fires pm:edit once per edited layer with { layer, shape }
    ctx.map.on('pm:edit', async (event: any) => {
        // Defer one tick so any pending snap commits (setTimeout 0) run first
        await new Promise(r => setTimeout(r, 0))
        const layer = event.layer as L.Layer
        if (!(layer as any)._dbId) return
        try {
            const phase = PHASES.find(p => featureLayers[p.key].some((f: LayerEntry) => f.layer === layer))
            const entry = phase ? featureLayers[phase.key].find((f: LayerEntry) => f.layer === layer) : null
            if (!entry) return

            if (layer instanceof L.Marker) {
                const ll = layer.getLatLng()
                entry.data.lat = ll.lat; entry.data.lng = ll.lng
            } else if (layer instanceof L.Polyline && !(layer instanceof L.Polygon)) {
                entry.data.coordinates = (layer.getLatLngs() as L.LatLng[]).map(ll => ({ lat: ll.lat, lng: ll.lng }))
            } else if (layer instanceof L.Polygon) {
                let coords = (layer.getLatLngs()[0] as L.LatLng[]).map(ll => ({ lat: ll.lat, lng: ll.lng }))
                // PostGIS/GEOS requires closed rings — first point must equal last
                if (coords.length >= 3) {
                    const first = coords[0], last = coords[coords.length - 1]
                    if (first.lat !== last.lat || first.lng !== last.lng)
                        coords = [...coords, { lat: first.lat, lng: first.lng }]
                }
                entry.data.coordinates = coords
            }

            await apiFetch(`/api/update/${(layer as any)._dbId}`, {
                method:  'PUT',
                headers: { 'Content-Type': 'application/json' },
                body:    JSON.stringify({ data: entry.data }),
            })

            if (phase)
                bindHoverPopup(layer, buildPopup(entry.data, phase, (layer as any)._dbId))
            if (phase?.key === 'areas') {
                createAreaPerimeterLabel(layer, entry.data.areaTypeKey ?? 'central_urban')
                await refreshScatteredAreas()
            }
            if (phase?.key === 'districts') {
                createPolygonEdgeLabel(layer, getDistrictLabel(entry.data.districtTypeKey ?? 'district', entry.data.label), '#f39c12')
            }
        } catch (err) { console.error('Edit persist error:', err) }

        ctx.lineEndpointLayer.clearLayers()
        ctx.drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l) })
    })

    // Geoman fires pm:remove once per removed layer with { layer, shape }
    ctx.map.on('pm:remove', async (event: any) => {
        const layer = event.layer as L.Layer
        let areaDeleted = false

        if ((layer as any)._dbId) {
            try {
                const res = await apiFetch(`/api/delete/${(layer as any)._dbId}`, { method: 'DELETE' })
                if (!res.ok) console.error(`Delete failed: ${(layer as any)._dbId}`, res.status)
                if (featureLayers.areas.some((f: LayerEntry) => f.layer === layer)) areaDeleted = true
            } catch (err) { console.error('Delete error:', err) }
        }
        if ((layer as any)._endpointMarkers) (layer as any)._endpointMarkers.forEach((m: L.Layer) => ctx.lineEndpointLayer.removeLayer(m))
        if ((layer as any)._perimeterLabel)  ctx.perimeterLabelLayer.removeLayer((layer as any)._perimeterLabel)
        if ((layer as any)._edgeLabelMarkers) (layer as any)._edgeLabelMarkers.forEach((m: L.Marker) => ctx.polygonEdgeLabelLayer.removeLayer(m))

        ctx.lineEndpointLayer.clearLayers()
        ctx.drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l) })

        for (const key of Object.keys(featureLayers))
            featureLayers[key] = featureLayers[key].filter((f: LayerEntry) => ctx.drawnItems.hasLayer(f.layer))

        if (areaDeleted) await refreshScatteredAreas()
        syncCounts()
    })
}

// ─── LOAD FROM DATABASE ───────────────────────────────────────────────────────

export async function loadFromDatabase(): Promise<void> {
    try {
        const res = await apiFetch('/api/load')
        if (!res.ok) { console.error('Load failed:', res.status); return }
        const features = await res.json() as DbFeature[]
        if (!features.length) { console.log('No saved features.'); return }

        ctx.drawnItems.clearLayers()
        ctx.lineEndpointLayer.clearLayers()
        for (const key of Object.keys(featureLayers)) featureLayers[key] = []

        let loaded = 0, skipped = 0

        for (const feature of features) {
            try {
                const data: FeatureData = typeof feature.data === 'string' ? JSON.parse(feature.data) : feature.data

                if (feature.layer === 'scattered') {
                    if (data.geometry) renderScatteredAreas(data.geometry)
                    continue
                }

                const phaseKey = API_LAYER_TO_PHASE[feature.layer] ?? data.type
                if (!phaseKey || !Object.prototype.hasOwnProperty.call(featureLayers, phaseKey)) { skipped++; continue }

                const phase = PHASES.find(p => p.key === phaseKey)
                if (!phase) { skipped++; continue }

                let layer: L.Layer

                if (phase.drawType === 'circle') {
                    if (!data.lat || !data.lng) { skipped++; continue }
                    layer = L.circle([data.lat, data.lng], {
                        radius: data.radius ?? 50,
                        color: '#e74c3c', weight: 2, fillColor: '#e74c3c', fillOpacity: 0.15,
                    })
                } else if (phase.drawType === 'marker') {
                    if (!data.lat || !data.lng) { skipped++; continue }
                    if (phaseKey === 'houseEntrances' && !data.entranceTypeKey)
                        data.entranceTypeKey = feature.layer as 'main_entrance' | 'secondary_entrance'
                    const entranceColor = phaseKey === 'houseEntrances' && data.entranceTypeKey === 'secondary_entrance' ? '#16a085' : phase.color
                    const icon = createEntranceIcon(data.label, entranceColor)
                    layer = L.marker([data.lat, data.lng], { icon })
                    if (phase.key === 'cityCenter' && data.lat != null && data.lng != null) {
                        store.cityCenterMode   = 'city_center'
                        store.cityCenterLatLng = { lat: data.lat, lng: data.lng }
                    }
                } else if (phase.drawType === 'polyline') {
                    if (!data.coordinates?.length) { skipped++; continue }
                    layer = L.polyline(data.coordinates.map(c => [c.lat, c.lng] as [number, number]), { color: phase.color, weight: POLYLINE_WEIGHT })
                } else {
                    if (!data.coordinates?.length) { skipped++; continue }
                    const style = phase.key === 'areas' ? areaStyle(data.areaTypeKey ?? feature.layer) : (polygonStyles[phaseKey] ?? { color: phase.color, weight: 3, fillOpacity: 0.15 })
                    layer = L.polygon(data.coordinates.map(c => [c.lat, c.lng] as [number, number]), style)
                }

                ;(layer as any)._dbId = feature.id
                ctx.drawnItems.addLayer(layer)
                bindContextMenu(layer, feature.id, phaseKey)
                // Endpoint arrows added after direction computation, not on load.
                createPermanentLabel(layer, data.label, phaseKey)
                if (phaseKey === 'areas')     createAreaPerimeterLabel(layer, data.areaTypeKey ?? feature.layer)
                if (phaseKey === 'districts') createPolygonEdgeLabel(layer, getDistrictLabel(data.districtTypeKey ?? 'district', data.label), '#f39c12')
                bindHoverPopup(layer, buildPopup(data, phase, feature.id))

                featureLayers[phaseKey].push({ layer, data })
                loaded++
            } catch (err) { console.error('Load feature error:', err); skipped++ }
        }

        let resumeAt = 0
        for (let i = 0; i < PHASES.length; i++) {
            const key = PHASES[i].key
            if      (featureLayers[key].length > 0)                              resumeAt = i
            else if (key === 'cityCenter' && store.cityCenterMode !== null)      resumeAt = i
            else                                                                 { resumeAt = i; break }
        }
        store.currentPhase = resumeAt

        // Restore the exact phase the user was on when they logged out
        const savedPhase = parseInt(localStorage.getItem('nars_resume_phase') ?? '', 10)
        if (!isNaN(savedPhase) && savedPhase >= 0 && savedPhase < PHASES.length)
            store.currentPhase = savedPhase
        localStorage.removeItem('nars_resume_phase')

        buildDrawControl(PHASES[store.currentPhase])
        syncCounts()
        refreshLayerVisibility()

        // If roads have already had directions computed (i.e. we are past the
        // roads phase or the user already clicked "Set Road Directions"), restore
        // their endpoint arrow markers now.  Directions are stored in the
        // coordinate order in the DB, so we just add arrows for the saved order.
        const resumedPhaseKey = PHASES[store.currentPhase]?.key
        const roadsPhaseIdx   = PHASES.findIndex(p => p.key === 'roads')
        const housePhaseIdx   = PHASES.findIndex(p => p.key === 'houseEntrances')
        const isPastRoads = store.currentPhase > roadsPhaseIdx
        // Only restore endpoint arrows if directions have actually been computed,
        // i.e. the user has moved past the Roads phase. While still on Roads,
        // arrows are added by computeAndApplyRoadDirections() — not on load.
        if (featureLayers.roads.length > 0 && isPastRoads) {
            for (const entry of featureLayers.roads as LayerEntry[]) {
                addPolylineEndpoints(entry.layer)
            }
        }

        console.log(`✓ Loaded ${loaded} features (${skipped} skipped)`)
    } catch (err) { console.error('Load error:', err) }
}

// ─── USER / COMMUNE BOOTSTRAP ────────────────────────────────────────────────

export async function loadUserAndCommune(): Promise<void> {
    try {
        const res = await apiFetch('/api/current_user')
        if (!res.ok) return
        const user = await res.json()
        store.user             = user
        store.municipalityName = user.commune?.name_fr ?? ''
        if (user.commune?.id)
            await displayCommuneBoundary(user.commune.id as number, user.commune.name_fr as string)
    } catch (err) { console.error('Commune nav error:', err) }
}
