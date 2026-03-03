// ─── LEAFLET ──────────────────────────────────────────────────────────────────
// Leaflet and leaflet-draw are loaded via CDN script tags in index.html.
// They expose a global window.L which we reference directly here.
// Do NOT import them via npm — leaflet-draw is a legacy UMD bundle that cannot
// be safely bundled by Vite (it patches window.L at load time).
const L = window.L

import { PHASES, API_LAYER_TO_PHASE, AREA_TYPES }       from './phases.js'
import { apiFetch }                                       from './api.js'
import { store, featureLayers, openModal, syncCounts, currentModalLayer }   from './store.js'
import { validateRoad, validateDistrict, checkDistrictCoverage, getRoadSide } from './validation.js'

// ─── MAP & LAYER INITIALIZATION ───────────────────────────────────────────────

export let map
let drawnItems, lineEndpointLayer, scatteredLayer, perimeterLabelLayer
let boundariesLayer = null
let drawControl     = null

const POLYLINE_WEIGHT = 8

export function initMap() {
    map = L.map('map').setView([36.7538, 3.0588], 10)

    // Tile layers
    const satellite = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', { attribution: 'Tiles © Esri' })
    const street    = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',  { attribution: '© OpenStreetMap contributors' })
    const carto     = L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    const dark      = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',  { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    satellite.addTo(map)
    L.control.layers({ Satellite: satellite, Street: street, Light: carto, Dark: dark }, null, { position: 'bottomleft' }).addTo(map)

    drawnItems        = new L.FeatureGroup().addTo(map)
    lineEndpointLayer = L.layerGroup().addTo(map)
    scatteredLayer    = L.layerGroup().addTo(map)
    perimeterLabelLayer = L.layerGroup().addTo(map)

    // Rebuild perimeter label angles whenever zoom changes
    map.on('zoomend', refreshAreaPerimeterLabels)

    buildDrawControl(PHASES[0])
    registerDrawEvents()
}

// ─── POLYGON STYLES ───────────────────────────────────────────────────────────

export function areaStyle(areaTypeKey) {
    const at = AREA_TYPES.find(a => a.key === areaTypeKey) || AREA_TYPES[0]
    return { color: at.color, weight: 2.5, fillOpacity: 0, dashArray: '10, 6' }
}

const polygonStyles = {
    districts:       { color: '#f39c12', weight: 3, fillOpacity: 0.15, fillColor: '#f39c12' },
    publicBuildings: { color: '#e67e22', weight: 3, fillOpacity: 0.25, fillColor: '#e67e22' },
    publicSpaces:    { color: '#2ecc71', weight: 3, fillOpacity: 0.20, fillColor: '#2ecc71' },
}

const scatteredStyle = {
    color: '#7f8c8d', weight: 1.5, fillOpacity: 0.10, fillColor: '#7f8c8d', dashArray: '3, 6',
}

// ─── ICONS ────────────────────────────────────────────────────────────────────

export function createEntranceIcon(label, color = '#27ae60') {
    const text = String(label || '').trim().slice(0, 6) || '?'
    return L.divIcon({
        className: 'entrance-marker',
        html: `<div style="background:${color};color:#fff;min-width:28px;height:28px;border-radius:14px;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:10px;border:2px solid #fff;box-shadow:0 2px 5px rgba(0,0,0,0.35);padding:0 5px;">${text}</div>`,
        iconSize: [28, 28], iconAnchor: [14, 14], popupAnchor: [0, -14],
    })
}

function createCityCenterIcon() {
    return L.divIcon({
        className: 'city-center-marker',
        html: `<div style="background:#e74c3c;color:#fff;width:36px;height:36px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:18px;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.4);">★</div>`,
        iconSize: [36, 36], iconAnchor: [18, 18], popupAnchor: [0, -18],
    })
}

function createEndpointIcon(char, angleDeg, color, large = false) {
    const size = large ? 36 : 24, fs = large ? 28 : 20, half = size / 2
    return L.divIcon({
        className: 'line-endpoint-marker',
        html: `<div style="color:${color};width:${size}px;height:${size}px;display:flex;align-items:center;justify-content:center;font-weight:900;font-size:${fs}px;line-height:1;text-shadow:-1px -1px 0 #fff,1px -1px 0 #fff,-1px 1px 0 #fff,1px 1px 0 #fff;transform:rotate(${angleDeg}deg);transform-origin:center;">${char}</div>`,
        iconSize: [size, size], iconAnchor: [half, half],
    })
}

// ─── POLYLINE ENDPOINT MARKERS ────────────────────────────────────────────────

function segmentAngle(a, b) {
    const fp = map.latLngToLayerPoint(a), tp = map.latLngToLayerPoint(b)
    return Math.atan2(tp.y - fp.y, tp.x - fp.x) * (180 / Math.PI)
}

function addPolylineEndpoints(layer) {
    if (!(layer instanceof L.Polyline) || layer instanceof L.Polygon) return
    const lls = layer.getLatLngs()
    if (!lls || lls.length < 2) return
    const c = layer.options.color || '#3498db'
    const s = L.marker(lls[0],              { icon: createEndpointIcon('>', segmentAngle(lls[0], lls[1]), c, true),  interactive: false })
    const e = L.marker(lls[lls.length - 1], { icon: createEndpointIcon('X', segmentAngle(lls[lls.length-2], lls[lls.length-1]), c, false), interactive: false })
    lineEndpointLayer.addLayer(s)
    lineEndpointLayer.addLayer(e)
    layer._endpointMarkers = [s, e]
}

// ─── LABELS ───────────────────────────────────────────────────────────────────

function createPermanentLabel(layer, label, phaseKey) {
    if (layer instanceof L.Marker) return
    if (phaseKey === 'areas') return   // areas use dedicated centered labels
    layer.bindTooltip(label, { permanent: true, direction: 'center', className: 'custom-shape-label' }).openTooltip()
}

export function createAreaPerimeterLabel(layer, areaTypeKey) {
    if (layer._perimeterLabel) {
        perimeterLabelLayer.removeLayer(layer._perimeterLabel)
        layer._perimeterLabel = null
    }

    if (!(layer instanceof L.Polygon)) return

    const mid = layer.getBounds().getCenter()

    const at    = AREA_TYPES.find(a => a.key === areaTypeKey) || AREA_TYPES[0]
    const label = at.label.replace(' Area', '')

    const marker = L.marker(mid, {
        icon: L.divIcon({
            className: '',
            html: `<div class="area-center-label">${label}</div>`,
            iconSize: [0, 0], iconAnchor: [0, 0],
        }),
        interactive: false, zIndexOffset: -100,
    })

    perimeterLabelLayer.addLayer(marker)
    layer._perimeterLabel = marker
}

function refreshAreaPerimeterLabels() {
    featureLayers.areas.forEach(({ layer, data }) => {
        createAreaPerimeterLabel(layer, data.areaTypeKey || 'central_urban')
    })
}

// ─── SPATIAL HELPERS ─────────────────────────────────────────────────────────

let municipalLimitRings = []
let scatteredPolygons   = []

function pointInRing(latlng, ring) {
    let inside = false
    const x = latlng.lat, y = latlng.lng
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const xi = ring[i].lat, yi = ring[i].lng, xj = ring[j].lat, yj = ring[j].lng
        if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
            inside = !inside
    }
    return inside
}

function pointInMunicipalLimit(latlng) {
    if (municipalLimitRings.length === 0) return true
    return municipalLimitRings.some(r => pointInRing(latlng, r))
}

function pointInScatteredArea(latlng) {
    return scatteredPolygons.some(r => pointInRing(latlng, r))
}

function polylineMidpoint(layer) {
    const lls = layer.getLatLngs()
    return lls[Math.floor(lls.length / 2)]
}

function extractRings(geoJsonGeom) {
    const rings = []
    if (!geoJsonGeom?.type || !geoJsonGeom?.coordinates) return rings
    const processRing = coords => {
        if (!coords?.length) return
        if (typeof coords[0][0] === 'number')
            rings.push(coords.map(c => L.latLng(c[1], c[0])))
        else
            coords.forEach(processRing)
    }
    if (geoJsonGeom.type === 'Polygon')
        processRing(geoJsonGeom.coordinates)
    else if (geoJsonGeom.type === 'MultiPolygon')
        geoJsonGeom.coordinates.forEach(p => processRing(p))
    else
        processRing(geoJsonGeom.coordinates)
    return rings
}

// ─── MUNICIPALITY BOUNDARY ────────────────────────────────────────────────────

export async function displayCommuneBoundary(communeId, communeName) {
    try {
        if (boundariesLayer) { map.removeLayer(boundariesLayer); boundariesLayer = null }
        const res = await apiFetch(`/api/commune/${communeId}/boundary`)
        if (!res.ok) return
        const data = await res.json()
        let geojson = typeof data.geometry === 'string' ? JSON.parse(data.geometry) : data.geometry
        if (!geojson?.type) return

        municipalLimitRings = extractRings(geojson)
        boundariesLayer = L.geoJSON(geojson, {
            style: { color: '#e74c3c', weight: 2.5, fillOpacity: 0.03, fillColor: '#e74c3c' },
            onEachFeature(_, layer) {
                const name = communeName || data.commune_name
                if (name) layer.bindTooltip(name, { permanent: false, direction: 'center', className: 'boundary-tooltip' })
            }
        }).addTo(map)
        map.fitBounds(boundariesLayer.getBounds(), { padding: [50, 50], maxZoom: 14 })
    } catch (e) { console.error('Boundary error:', e) }
}

// ─── SCATTERED AREAS ──────────────────────────────────────────────────────────

function renderScatteredAreas(geoJsonStr) {
    scatteredLayer.clearLayers()
    scatteredPolygons = []
    if (!geoJsonStr) return
    try {
        const geojson = typeof geoJsonStr === 'string' ? JSON.parse(geoJsonStr) : geoJsonStr
        if (!geojson?.type) return
        scatteredPolygons = extractRings(geojson)
        L.geoJSON(geojson, {
            style: scatteredStyle,
            onEachFeature(_, layer) {
                layer.bindTooltip('Scattered Area', { direction: 'center', className: 'boundary-tooltip' })
            }
        }).addTo(scatteredLayer)
    } catch (e) { console.error('Scattered render error:', e) }
}

async function refreshScatteredAreas() {
    try {
        const res = await apiFetch('/api/areas/refresh-scattered', { method: 'POST' })
        if (!res.ok) return
        const data = await res.json()
        if (data.geojson) renderScatteredAreas(data.geojson)
        else scatteredLayer.clearLayers()
    } catch (e) { console.error('Scatter refresh error:', e) }
}

// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────

function buildDrawControl(phase) {
    if (drawControl) { map.removeControl(drawControl); drawControl = null }
    const opts = { polygon: false, polyline: false, rectangle: false, circle: false, circlemarker: false, marker: false }
    if (phase.drawType === 'polygon') {
        opts.polygon = {
            allowIntersection: false,
            shapeOptions: {
                color: phase.color, weight: 2.5,
                fillOpacity: phase.key === 'areas' ? 0 : 0.15,
                dashArray:   phase.key === 'areas' ? '10, 6' : null,
            },
        }
    }
    if (phase.drawType === 'polyline')
        opts.polyline = { shapeOptions: { color: phase.color, weight: POLYLINE_WEIGHT } }
    if (phase.drawType === 'marker') {
        const icon = phase.key === 'cityCenter' ? createCityCenterIcon() : createEntranceIcon('?', phase.color)
        opts.marker = { icon }
    }
    drawControl = new L.Control.Draw({ edit: { featureGroup: drawnItems, edit: true, remove: true }, draw: opts })
    map.addControl(drawControl)
}

// ─── PLACEMENT VALIDATION ─────────────────────────────────────────────────────

async function validatePlacement(layer, phase) {
    const checkPoint = phase.drawType === 'marker'   ? layer.getLatLng()
                     : phase.drawType === 'polyline' ? polylineMidpoint(layer)
                     : layer.getBounds().getCenter()

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

// ─── POPUP BUILDER ────────────────────────────────────────────────────────────

function buildPopup(data, phase) {
    const lines = [`<b>${data.label}</b>`, `<small>${phase.label}</small>`]
    if (data.decisionNumber)    lines.push(`<small>Decision: ${data.decisionNumber}</small>`)
    if (data.decisionDate)      lines.push(`<small>Date: ${data.decisionDate}</small>`)
    if (data.roadLabel)         lines.push(`<small>Road: ${data.roadLabel}</small>`)
    if (data.side)              lines.push(`<small>Side: ${data.side} (${data.side === 'left' ? 'odd' : 'even'})</small>`)
    if (data.mainEntranceLabel) lines.push(`<small>Main entrance: ${data.mainEntranceLabel}</small>`)
    return lines.join('<br>')
}

// ─── FEATURE DATA + API SAVE SHAPE ───────────────────────────────────────────

function buildFeatureData(layer, phase, modalResult) {
    const base = { type: phase.key, label: modalResult.label,
                   decisionNumber: modalResult.decisionNumber, decisionDate: modalResult.decisionDate,
                   ...modalResult }
    if (phase.drawType === 'marker') {
        const ll = layer.getLatLng()
        return { ...base, lat: ll.lat, lng: ll.lng }
    }
    const lls = phase.drawType === 'polygon' ? layer.getLatLngs()[0] : layer.getLatLngs()
    return { ...base, coordinates: lls.map(ll => ({ lat: ll.lat, lng: ll.lng })) }
}

function toApiSaveShape(featureData) {
    switch (featureData.type) {
        case 'areas':              return { type: 'area',            layer: featureData.areaTypeKey     || 'central_urban' }
        case 'cityCenter':         return { type: 'city_center',     layer: 'city_center' }
        case 'districts':          return { type: 'district',        layer: featureData.districtTypeKey || 'district' }
        case 'roads':              return { type: 'road',            layer: featureData.roadTypeKey     || 'street' }
        case 'mainEntrances':      return { type: 'house_entrance',  layer: 'main_entrance' }
        case 'secondaryEntrances': return { type: 'house_entrance',  layer: 'secondary_entrance' }
        case 'publicBuildings':    return { type: 'public_building', layer: 'public_building' }
        case 'publicSpaces':       return { type: 'public_space',    layer: featureData.spaceTypeKey    || 'garden' }
        default: return null
    }
}

async function saveToDatabase(featureData) {
    try {
        const shape = toApiSaveShape(featureData)
        if (!shape) return { ok: false, error: `Unknown type '${featureData.type}'.` }

        const res = await apiFetch('/api/save', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: shape.type, layer: shape.layer, label: featureData.label, data: featureData }),
        })
        if (!res.ok) {
            const raw = await res.text()
            let detail = raw || `HTTP ${res.status}`
            try { const p = JSON.parse(raw); detail = p?.detail || p?.title || detail } catch {}
            return { ok: false, error: `HTTP ${res.status}: ${String(detail).slice(0, 240)}` }
        }
        return { ok: true, data: await res.json() }
    } catch (err) { return { ok: false, error: err?.message || 'Network error' } }
}

// ─── LOAD FROM DATABASE ───────────────────────────────────────────────────────

export async function loadFromDatabase() {
    try {
        const res = await apiFetch('/api/load')
        if (!res.ok) { console.error('Load failed:', res.status); return }
        const features = await res.json()
        if (!features.length) { console.log('No saved features.'); return }

        drawnItems.clearLayers()
        lineEndpointLayer.clearLayers()
        for (const key of Object.keys(featureLayers)) featureLayers[key] = []

        let loaded = 0, skipped = 0

        features.forEach(feature => {
            try {
                const data = typeof feature.data === 'string' ? JSON.parse(feature.data) : feature.data

                if (feature.layer === 'scattered') {
                    if (data.geometry) renderScatteredAreas(data.geometry)
                    return
                }

                const phaseKey = API_LAYER_TO_PHASE[feature.layer] || data.type
                if (!phaseKey || !featureLayers.hasOwnProperty(phaseKey)) { skipped++; return }

                const phase = PHASES.find(p => p.key === phaseKey)
                if (!phase) { skipped++; return }

                let layer
                if (phase.drawType === 'marker') {
                    if (!data.lat || !data.lng) { skipped++; return }
                    const icon = phase.key === 'cityCenter' ? createCityCenterIcon()
                               : createEntranceIcon(data.label, phase.color)
                    layer = L.marker([data.lat, data.lng], { icon })
                    if (phase.key === 'cityCenter') {
                        store.cityCenterMode   = 'city_center'
                        store.cityCenterLatLng = { lat: data.lat, lng: data.lng }
                    }
                } else if (phase.drawType === 'polyline') {
                    if (!data.coordinates?.length) { skipped++; return }
                    layer = L.polyline(data.coordinates.map(c => [c.lat, c.lng]),
                        { color: phase.color, weight: POLYLINE_WEIGHT })
                } else {
                    if (!data.coordinates?.length) { skipped++; return }
                    const style = phase.key === 'areas'
                        ? areaStyle(data.areaTypeKey || feature.layer)
                        : (polygonStyles[phaseKey] || { color: phase.color, weight: 3, fillOpacity: 0.15 })
                    layer = L.polygon(data.coordinates.map(c => [c.lat, c.lng]), style)
                }

                layer._dbId = feature.id
                drawnItems.addLayer(layer)
                if (phase.drawType === 'polyline') addPolylineEndpoints(layer)
                createPermanentLabel(layer, data.label, phaseKey)
                if (phaseKey === 'areas') createAreaPerimeterLabel(layer, data.areaTypeKey || feature.layer)
                layer.bindPopup(buildPopup(data, phase))

                featureLayers[phaseKey].push({ layer, data })
                loaded++
            } catch (err) { console.error('Load feature error:', err); skipped++ }
        })

        // Resume at the furthest phase that has data
        for (let i = PHASES.length - 1; i >= 0; i--) {
            if (featureLayers[PHASES[i].key].length > 0) { store.currentPhase = i; break }
        }
        if (store.currentPhase >= 2 && store.cityCenterMode === null)
            store.cityCenterMode = 'auto'

        buildDrawControl(PHASES[store.currentPhase])
        syncCounts()
        console.log(`✓ Loaded ${loaded} features (${skipped} skipped)`)
    } catch (err) { console.error('Load error:', err) }
}

// ─── PHASE NAVIGATION ─────────────────────────────────────────────────────────

export async function navigatePhase(direction) {
    const target = store.currentPhase + direction
    if (target < 0 || target >= PHASES.length) return

    if (direction > 0) {
        const from = PHASES[store.currentPhase]

        if (from.key === 'areas' && featureLayers.areas.length === 0) {
            alert('Please draw at least one urban area before proceeding.'); return
        }
        if (from.key === 'cityCenter' && store.cityCenterMode === null) {
            alert('Please place a city center marker or skip the phase.'); return
        }
        if (from.key === 'districts') {
            const coverage = await checkDistrictCoverage()
            if (!coverage.covered) { alert(`⛔ ${coverage.message}`); return }
        }
        if (from.key === 'roads' && featureLayers.roads.length === 0) {
            alert('Please draw at least one road before proceeding.'); return
        }
        if (from.key === 'mainEntrances' && featureLayers.mainEntrances.length === 0) {
            alert('Please place at least one main entrance before proceeding.'); return
        }
    }

    setPhase(target)
}

export async function goToPhase(target) {
    if (target === store.currentPhase) return
    if (target > store.currentPhase) {
        for (let i = store.currentPhase; i < target; i++) {
            const before = store.currentPhase
            await navigatePhase(1)
            if (store.currentPhase === before) return // gate blocked
        }
    } else {
        setPhase(target)
    }
}

export function setPhase(index) {
    store.currentPhase = index
    buildDrawControl(PHASES[index])

    if (PHASES[index].key === 'cityCenter' && store.cityCenterMode === null)
        store.cityCenterDialogVisible = true
}

export function cityCenterYes() {
    store.cityCenterDialogVisible = false
}

export function cityCenterSkip() {
    store.cityCenterDialogVisible = false
    store.cityCenterMode = 'auto'
    setPhase(2)
}

// ─── DRAW EVENTS ─────────────────────────────────────────────────────────────

function registerDrawEvents() {

    map.on(L.Draw.Event.CREATED, async (event) => {
        const layer = event.layer
        const phase = PHASES[store.currentPhase]

        if (!await validatePlacement(layer, phase)) return

        if (phase.key === 'roads') {
            const check = await validateRoad(layer)
            if (!check.valid) { alert(`⛔ Road cannot be saved:\n${check.error}`); return }
        }
        if (phase.key === 'districts') {
            const check = await validateDistrict(layer)
            if (!check.valid) { alert(`⛔ District cannot be saved:\n${check.error}`); return }
        }

        await prepareModalExtras(phase, layer)

        const modalResult = await openModal(store.currentPhase, layer)
        if (!modalResult) return

        applyStyle(layer, phase, modalResult)

        const featureData = buildFeatureData(layer, phase, modalResult)
        const saveResult  = await saveToDatabase(featureData)
        if (!saveResult.ok) { alert(`Failed to save feature.\n${saveResult.error || 'Please try again.'}`); return }

        layer._dbId = saveResult.data.id
        drawnItems.addLayer(layer)
        if (phase.drawType === 'polyline') addPolylineEndpoints(layer)
        createPermanentLabel(layer, modalResult.label, phase.key)
        if (phase.key === 'areas') createAreaPerimeterLabel(layer, modalResult.areaTypeKey)
        layer.bindPopup(buildPopup(featureData, phase))

        featureLayers[phase.key].push({ layer, data: featureData })

        if (phase.key === 'cityCenter') {
            const ll = layer.getLatLng()
            store.cityCenterMode   = 'city_center'
            store.cityCenterLatLng = { lat: ll.lat, lng: ll.lng }
            setTimeout(() => setPhase(2), 400)
        }

        if (phase.key === 'areas') await refreshScatteredAreas()

        syncCounts()
    })

    map.on(L.Draw.Event.EDITED, async (event) => {
        event.layers.eachLayer(async (layer) => {
            if (!layer._dbId) return
            try {
                const phase = PHASES.find(p => featureLayers[p.key].some(f => f.layer === layer))
                const entry = phase ? featureLayers[phase.key].find(f => f.layer === layer) : null
                if (!entry) return

                if (layer instanceof L.Marker) {
                    const ll = layer.getLatLng()
                    entry.data.lat = ll.lat; entry.data.lng = ll.lng
                } else if (layer instanceof L.Polyline && !(layer instanceof L.Polygon)) {
                    entry.data.coordinates = layer.getLatLngs().map(ll => ({ lat: ll.lat, lng: ll.lng }))
                } else if (layer instanceof L.Polygon) {
                    entry.data.coordinates = layer.getLatLngs()[0].map(ll => ({ lat: ll.lat, lng: ll.lng }))
                }

                await apiFetch(`/api/update/${layer._dbId}`, {
                    method: 'PUT', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ data: entry.data }),
                })

                if (phase?.key === 'areas') {
                    createAreaPerimeterLabel(layer, entry.data.areaTypeKey || 'central_urban')
                    await refreshScatteredAreas()
                }
            } catch (err) { console.error('Edit persist error:', err) }
        })

        lineEndpointLayer.clearLayers()
        drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l) })
    })

    map.on(L.Draw.Event.DELETED, async (event) => {
        let areaDeleted = false
        event.layers.eachLayer(async (layer) => {
            if (layer._dbId) {
                try {
                    const res = await apiFetch(`/api/delete/${layer._dbId}`, { method: 'DELETE' })
                    if (!res.ok) console.error(`Delete failed: ${layer._dbId}`, res.status)
                    if (featureLayers.areas.some(f => f.layer === layer)) areaDeleted = true
                } catch (err) { console.error('Delete error:', err) }
            }
            if (layer._endpointMarkers) layer._endpointMarkers.forEach(m => lineEndpointLayer.removeLayer(m))
            if (layer._perimeterLabel)  perimeterLabelLayer.removeLayer(layer._perimeterLabel)
        })

        lineEndpointLayer.clearLayers()
        drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l) })

        for (const key of Object.keys(featureLayers))
            featureLayers[key] = featureLayers[key].filter(f => drawnItems.hasLayer(f.layer))

        if (areaDeleted) await refreshScatteredAreas()
        syncCounts()
    })
}

// ─── MODAL EXTRA PREPARATION ──────────────────────────────────────────────────

async function prepareModalExtras(phase, layer) {
    const m = store.modal

    if (phase.key === 'areas') {
        const { checkMainUrbanExists } = await import('./validation.js')
        m.mainUrbanExists = await checkMainUrbanExists()
        if (!m.mainUrbanExists && store.municipalityName)
            m.label = store.municipalityName
        m.areaTypeKey = m.mainUrbanExists ? 'secondary_urban' : 'central_urban'
    }

    if (phase.key === 'mainEntrances') {
        m.roadOptions = featureLayers.roads.map((r, i) => ({
            idx:   i,
            label: r.data.label || `Road ${i + 1}`,
            dbId:  r.layer._dbId,
        }))
    }

    if (phase.key === 'secondaryEntrances') {
        m.mainEntranceOptions = featureLayers.mainEntrances.map((e, i) => ({
            idx:   i,
            label: e.data.label || `Entrance ${i + 1}`,
            dbId:  e.layer._dbId,
        }))
    }
}

export async function fetchRoadSide(roadDbId, roadIdx) {
    const layer = store.modal
    layer.entranceSideLoading = true
    layer.entranceSide   = null
    layer.entranceNumber = null

    try {
        const ll = currentModalLayer ? currentModalLayer.getLatLng() : null
        if (!ll) return
        const result = await getRoadSide(roadDbId, ll.lat, ll.lng)
        if (result) {
            layer.entranceSide   = result.side
            layer.entranceNumber = result.suggestedNumber
        }
    } finally {
        layer.entranceSideLoading = false
    }
}

export function computeBisNumber(mainEntranceDbId) {
    const count = featureLayers.secondaryEntrances.filter(s =>
        s.data.mainEntranceDbId === mainEntranceDbId).length
    store.modal.bisNumber = count + 1
    store.modal.label = 'BIS' + String(count + 1).padStart(2, '0')
}

// ─── STYLE APPLICATOR ────────────────────────────────────────────────────────

function applyStyle(layer, phase, modalResult) {
    if      (phase.key === 'areas')              layer.setStyle(areaStyle(modalResult.areaTypeKey))
    else if (phase.key === 'districts')          layer.setStyle(polygonStyles.districts)
    else if (phase.key === 'publicBuildings')    layer.setStyle(polygonStyles.publicBuildings)
    else if (phase.key === 'publicSpaces')       layer.setStyle(polygonStyles.publicSpaces)
    else if (phase.drawType === 'polyline')      layer.setStyle({ color: phase.color, weight: POLYLINE_WEIGHT })
    else if (phase.key === 'mainEntrances')
        layer.setIcon(createEntranceIcon(String(modalResult.entranceNumber || modalResult.label), phase.color))
    else if (phase.key === 'secondaryEntrances') {
        const bisStr = 'BIS' + String(modalResult.bisNumber || 1).padStart(2, '0')
        layer.setIcon(createEntranceIcon(bisStr, phase.color))
    }
    else if (phase.key === 'cityCenter')
        layer.setIcon(createCityCenterIcon())
}

// ─── USER / COMMUNE BOOTSTRAP ────────────────────────────────────────────────

export async function loadUserAndCommune() {
    try {
        const res = await apiFetch('/api/current_user')
        if (!res.ok) return
        const user = await res.json()
        store.user             = user
        store.municipalityName = user.commune?.name_fr || ''

        if (user.commune?.id)
            await displayCommuneBoundary(user.commune.id, user.commune.name_fr)
        if (user.commune?.latitude && user.commune?.longitude)
            map.setView([parseFloat(user.commune.latitude), parseFloat(user.commune.longitude)], 13)
    } catch (err) { console.error('Commune nav error:', err) }
}
