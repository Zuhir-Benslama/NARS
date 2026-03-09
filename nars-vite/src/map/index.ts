// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────
// Initialises the map, wires draw events, handles phase navigation and loading.
// All heavy logic lives in the map-* sub-modules.

import { PHASES, API_LAYER_TO_PHASE } from '../phases'
import { apiFetch }                   from '../api'
import { store, featureLayers, openModal, syncCounts } from '../store'
import { validateRoad, validateDistrict, checkDistrictCoverage } from '../validation'
import type { FeatureData, LayerEntry, DbFeature }    from '../types'

import { ctx, POLYLINE_WEIGHT }       from './state'
import { areaStyle, polygonStyles, createEntranceIcon, createCityCenterIcon, applyStyle, buildPopup } from './styles'
import { addPolylineEndpoints, createPermanentLabel, createAreaPerimeterLabel, createPolygonEdgeLabel, refreshAllEdgeLabels, refreshLayerVisibility } from './labels'
import { pointInMunicipalLimit, pointInScatteredArea, polylineMidpoint, displayCommuneBoundary, renderScatteredAreas, refreshScatteredAreas } from './geometry'
import { enableSnapping, disableSnapping, hookEditHandles, hookAllEditMarkers, editModeActive, installSnapInterceptors } from './snapping'
import { bindContextMenu }            from './context-menu'
import { buildFeatureData, saveToDatabase, prepareModalExtras } from './features'

// Re-export public API consumed by Vue components and main.ts
export { displayCommuneBoundary }                           from './geometry'
export { bindContextMenu }                                  from './context-menu'
export { fetchRoadSide, computeBisNumber }                  from './features'
export { createEntranceIcon, areaStyle }                    from './styles'
export { createPolygonEdgeLabel, createAreaPerimeterLabel } from './labels'

declare const L: typeof import('leaflet') & {
    Draw: any
    Control: typeof import('leaflet').Control & { Draw: new (opts: any) => any }
    DrawEvents: any
}

// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────

export function initMap(): void {
    ctx.map = L.map('map').setView([28.0, 2.5], 5)

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
    ctx.drawControl           = null

    ctx.map.on('zoomend', refreshAllEdgeLabels)
    installSnapInterceptors()

    buildDrawControl(PHASES[0])
    registerDrawEvents()
}

// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────

function buildDrawControl(phase: typeof PHASES[number]): void {
    // Remove existing Geoman controls
    ctx.map.pm.removeControls()

    // Configure Geoman options based on the current phase
    const drawOptions: any = {}
    
    if (phase.drawType === 'polygon') {
        drawOptions.polygon = {
            allowIntersection: false,
            pathOptions: {
                color: phase.color,
                weight: 2.5,
                fillOpacity: phase.key === 'areas' ? 0 : 0.15,
                dashArray: phase.key === 'areas' ? '10, 6' : undefined,
            },
        }
    }
    if (phase.drawType === 'polyline') {
        drawOptions.polyline = {
            pathOptions: { color: phase.color, weight: POLYLINE_WEIGHT },
        }
    }
    if (phase.drawType === 'marker') {
        const icon = phase.key === 'cityCenter' ? createCityCenterIcon() : createEntranceIcon('?', phase.color)
        drawOptions.marker = { icon }
    }

    // Add Geoman controls
    // Enable drawing for current phase, enable editing for all drawn items, disable removal
    ctx.map.pm.addControls({
        drawMarker: phase.drawType === 'marker',
        drawPolygon: phase.drawType === 'polygon',
        drawPolyline: phase.drawType === 'polyline',
        drawRectangle: false,
        drawCircle: false,
        drawCircleMarker: false,
        editMode: true,
        removalMode: false,
        ...drawOptions,
    })

    // Store the draw mode for later use
    ;(ctx.map as any)._geomanDrawMode = phase.drawType
}

// ─── PLACEMENT VALIDATION ─────────────────────────────────────────────────────

async function validatePlacement(layer: L.Layer, phase: typeof PHASES[number]): Promise<boolean> {
    let checkPoint: L.LatLng
    if (phase.drawType === 'marker')        checkPoint = (layer as L.Marker).getLatLng()
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
    if (phase.key !== 'publicBuildings' && phase.key !== 'areas' && phase.key !== 'cityCenter') {
        if (pointInScatteredArea(checkPoint)) {
            alert(`⛔ This ${phase.label.replace(/s$/, '').toLowerCase()} cannot be placed in a scattered area.\nOnly public buildings are allowed in scattered areas.`)
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
        if (from.key === 'cityCenter'   && store.cityCenterMode === null)               { alert('Please place a city center marker or skip the phase.'); return }
        if (from.key === 'districts') {
            const coverage = await checkDistrictCoverage()
            if (!coverage.covered) { alert(`⛔ ${coverage.message}`); return }
        }
        if (from.key === 'roads'          && featureLayers.roads.length === 0)           { alert('Please draw at least one road before proceeding.'); return }
        if (from.key === 'houseEntrances' && featureLayers.houseEntrances.length === 0) { alert('Please place at least one house entrance before proceeding.'); return }
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
    if (PHASES[index].key === 'cityCenter' && store.cityCenterMode === null)
        store.cityCenterDialogVisible = true
    if      (PHASES[index].key === 'districts') enableSnapping('districts', undefined, 'districts')
    else if (PHASES[index].key === 'roads')     enableSnapping('roads',     undefined, 'roads')
    else disableSnapping()
    refreshLayerVisibility()
}

export function cityCenterYes(): void {
    store.cityCenterDialogVisible = false
}

export function cityCenterSkip(): void {
    store.cityCenterDialogVisible = false
    store.cityCenterMode = 'auto'
    setPhase(PHASES.findIndex(p => p.key === 'roads'))
}

// ─── DRAW EVENTS ──────────────────────────────────────────────────────────────

function registerDrawEvents(): void {
    // Geoman uses 'pm:drawstart', 'pm:drawend', 'pm:editstart', 'pm:editend', etc.
    
    ctx.map.on('pm:drawstart', (e: any) => {
        const key = PHASES[store.currentPhase]?.key
        if      (key === 'areas')     enableSnapping('districts', undefined, 'areas')
        else if (key === 'districts') enableSnapping('districts', undefined, 'districts')
        else if (key === 'roads')     enableSnapping('roads',     undefined, 'roads')
    })
    
    ctx.map.on('pm:drawend', () => {
        // Don't disable on drawend if we're in edit mode — editend handles that
        if (!editModeActive) disableSnapping()
    })

    ctx.map.on('pm:editstart', (e: any) => {
        const key = PHASES[store.currentPhase]?.key

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
        setTimeout(refreshLayerVisibility, 0)
    })

    // Geoman uses 'pm:create' instead of 'L.Draw.Event.CREATED'
    ctx.map.on('pm:create', async (event: any) => {
        const layer = event.layer as L.Layer
        const phase = PHASES[store.currentPhase]

        if (!await validatePlacement(layer, phase)) return

        if (phase.key === 'roads') {
            const check = await validateRoad(layer as L.Polyline)
            if (!check.valid) { alert(`⛔ Road cannot be saved:\n${check.error}`); return }
        }
        if (phase.key === 'districts') {
            const check = await validateDistrict(layer as L.Polygon)
            if (!check.valid) { alert(`⛔ District cannot be saved:\n${check.error}`); return }
        }

        await prepareModalExtras(phase, layer)

        const modalResult = await openModal(store.currentPhase, layer)
        if (!modalResult) return

        applyStyle(layer, phase, modalResult as unknown as FeatureData)

        const featureData = buildFeatureData(layer, phase, modalResult as unknown as Record<string, unknown>)
        const saveResult  = await saveToDatabase(featureData)
        if (!saveResult.ok) { alert(`Failed to save feature.\n${saveResult.error ?? 'Please try again.'}`); return }

        ;(layer as any)._dbId = saveResult.data!.id
        ctx.drawnItems.addLayer(layer)
        bindContextMenu(layer, saveResult.data!.id, phase.key)
        if (phase.drawType === 'polyline') addPolylineEndpoints(layer)
        createPermanentLabel(layer, modalResult.label as string, phase.key)
        if (phase.key === 'areas')     createAreaPerimeterLabel(layer, (modalResult as any).areaTypeKey as string)
        if (phase.key === 'districts') createPolygonEdgeLabel(layer, modalResult.label as string, '#f39c12')
        ;(layer as L.Path).bindPopup(buildPopup(featureData, phase, saveResult.data!.id))

        featureLayers[phase.key].push({ layer, data: featureData })

        if (phase.key === 'cityCenter') {
            const ll = (layer as L.Marker).getLatLng()
            store.cityCenterMode   = 'city_center'
            store.cityCenterLatLng = { lat: ll.lat, lng: ll.lng }
            setTimeout(() => setPhase(PHASES.findIndex(p => p.key === 'roads')), 400)
        }

        if (phase.key === 'areas') await refreshScatteredAreas()

        syncCounts()
    })

    // Geoman uses 'pm:edit' instead of 'L.Draw.Event.EDITED'
    ctx.map.on('pm:edit', async (event: any) => {
        const e = event as any
        // Defer one tick so any pending snap commits (setTimeout 0) run first
        await new Promise(r => setTimeout(r, 0))
        e.layers.eachLayer(async (layer: L.Layer) => {
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
                    (layer as L.Path).bindPopup(buildPopup(entry.data, phase, (layer as any)._dbId))
                if (phase?.key === 'areas') {
                    createAreaPerimeterLabel(layer, entry.data.areaTypeKey ?? 'central_urban')
                    await refreshScatteredAreas()
                }
                if (phase?.key === 'districts') {
                    createPolygonEdgeLabel(layer, entry.data.label, '#f39c12')
                }
            } catch (err) { console.error('Edit persist error:', err) }
        })

        ctx.lineEndpointLayer.clearLayers()
        ctx.drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l) })
    })

    // Geoman uses 'pm:remove' instead of 'L.Draw.Event.DELETED'
    ctx.map.on('pm:remove', async (event: any) => {
        const e = event as any
        let areaDeleted = false
        e.layers.eachLayer(async (layer: L.Layer) => {
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
        })

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

                if (phase.drawType === 'marker') {
                    if (!data.lat || !data.lng) { skipped++; continue }
                    if (phaseKey === 'houseEntrances' && !data.entranceTypeKey)
                        data.entranceTypeKey = feature.layer as 'main_entrance' | 'secondary_entrance'
                    const entranceColor = phaseKey === 'houseEntrances' && data.entranceTypeKey === 'secondary_entrance' ? '#16a085' : phase.color
                    const icon = phase.key === 'cityCenter' ? createCityCenterIcon() : createEntranceIcon(data.label, entranceColor)
                    layer = L.marker([data.lat, data.lng], { icon })
                    if (phase.key === 'cityCenter') {
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
                if (phase.drawType === 'polyline') addPolylineEndpoints(layer)
                createPermanentLabel(layer, data.label, phaseKey)
                if (phaseKey === 'areas')     createAreaPerimeterLabel(layer, data.areaTypeKey ?? feature.layer)
                if (phaseKey === 'districts') createPolygonEdgeLabel(layer, data.label, '#f39c12')
                ;(layer as L.Path).bindPopup(buildPopup(data, phase, feature.id))

                featureLayers[phaseKey].push({ layer, data })
                loaded++
            } catch (err) { console.error('Load feature error:', err); skipped++ }
        }

        const roadsIdx = PHASES.findIndex(p => p.key === 'roads')
        const cityCenterSkipped = featureLayers['cityCenter'].length === 0 && featureLayers['roads'].length > 0
        if (cityCenterSkipped) store.cityCenterMode = 'auto'

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

        if (store.currentPhase >= roadsIdx && store.cityCenterMode === null) store.cityCenterMode = 'auto'

        buildDrawControl(PHASES[store.currentPhase])
        syncCounts()
        refreshLayerVisibility()
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
