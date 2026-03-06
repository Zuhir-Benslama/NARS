import { PHASES, API_LAYER_TO_PHASE, AREA_TYPES } from './phases'
import { apiFetch }                                from './api'
import { store, featureLayers, openModal, openEditModal, syncCounts, currentModalLayer } from './store'
import { validateRoad, validateDistrict, checkDistrictCoverage, getRoadSide, checkMainUrbanExists } from './validation'
import type { FeatureData, LayerEntry, SaveResult, DbFeature, ScatteredRefreshResponse } from './types'

// ─── LEAFLET ──────────────────────────────────────────────────────────────────
// Both leaflet and leaflet-draw are loaded via CDN <script> tags in index.html
// and patch window.L before this module runs. We declare L as an ambient global
// so TypeScript knows its type without generating any import statement that
// Rollup/Vite would fail to resolve at build time.
declare const L: typeof import('leaflet') & {
    Draw: any
    Control: typeof import('leaflet').Control & { Draw: new (opts: any) => any }
    DrawEvents: any
}

// ─── MAP & LAYER INITIALIZATION ───────────────────────────────────────────────

export let map: L.Map
let drawnItems:          L.FeatureGroup
let lineEndpointLayer:   L.LayerGroup
let scatteredLayer:      L.LayerGroup
let perimeterLabelLayer: L.LayerGroup
let polygonEdgeLabelLayer: L.LayerGroup
let boundariesLayer:     L.GeoJSON | null = null
let drawControl:         L.Control.Draw  | null = null

const POLYLINE_WEIGHT = 8

export function initMap(): void {
    map = L.map('map').setView([28.0, 2.5], 5)

    const satellite = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', { attribution: 'Tiles © Esri' })
    const street    = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',  { attribution: '© OpenStreetMap contributors' })
    const carto     = L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    const dark      = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',  { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' })
    satellite.addTo(map)
    L.control.layers({ Satellite: satellite, Street: street, Light: carto, Dark: dark }, undefined, { position: 'bottomleft' }).addTo(map)

    drawnItems           = new L.FeatureGroup().addTo(map)
    lineEndpointLayer    = L.layerGroup().addTo(map)
    scatteredLayer       = L.layerGroup().addTo(map)
    perimeterLabelLayer  = L.layerGroup().addTo(map)
    polygonEdgeLabelLayer = L.layerGroup().addTo(map)

    map.on('zoomend', refreshAllEdgeLabels)

    buildDrawControl(PHASES[0])
    registerDrawEvents()
}

// ─── POLYGON STYLES ───────────────────────────────────────────────────────────

export function areaStyle(areaTypeKey: string): L.PathOptions {
    const at = AREA_TYPES.find(a => a.key === areaTypeKey) ?? AREA_TYPES[0]
    return { color: at.color, weight: 2.5, fillOpacity: 0, dashArray: '10, 6' }
}

const polygonStyles: Record<string, L.PathOptions> = {
    districts:       { color: '#f39c12', weight: 3, fillOpacity: 0 },
    publicBuildings: { color: '#e67e22', weight: 3, fillOpacity: 0.25, fillColor: '#e67e22' },
    publicSpaces:    { color: '#2ecc71', weight: 3, fillOpacity: 0.20, fillColor: '#2ecc71' },
}

const scatteredStyle: L.PathOptions = {
    color: '#7f8c8d', weight: 1.5, fillOpacity: 0.10, fillColor: '#7f8c8d', dashArray: '3, 6',
}

// ─── ICONS ────────────────────────────────────────────────────────────────────

export function createEntranceIcon(label: string | number, color = '#27ae60'): L.DivIcon {
    const text = String(label ?? '').trim().slice(0, 6) || '?'
    return L.divIcon({
        className: 'entrance-marker',
        html: `<div class="entrance-icon" style="background:${color}">${text}</div>`,
        iconSize:    [28, 28],
        iconAnchor:  [14, 14],
        popupAnchor: [0, -14],
    })
}

function createCityCenterIcon(): L.DivIcon {
    return L.divIcon({
        className: 'city-center-marker',
        html: '<div class="city-center-icon">★</div>',
        iconSize:   [36, 36],
        iconAnchor: [18, 18],
        popupAnchor: [0, -18],
    })
}

function createEndpointIcon(char: string, angleDeg: number, color: string, large = false): L.DivIcon {
    const size = large ? 36 : 24, fs = large ? 28 : 20, half = size / 2
    return L.divIcon({
        className: 'line-endpoint-marker',
        html: `<div class="endpoint-icon" style="color:${color};width:${size}px;height:${size}px;font-size:${fs}px;transform:rotate(${angleDeg}deg)">${char}</div>`,
        iconSize:   [size, size],
        iconAnchor: [half, half],
    })
}

// ─── POLYLINE ENDPOINT MARKERS ────────────────────────────────────────────────

function segmentAngle(a: L.LatLng, b: L.LatLng): number {
    const fp = map.latLngToLayerPoint(a), tp = map.latLngToLayerPoint(b)
    return Math.atan2(tp.y - fp.y, tp.x - fp.x) * (180 / Math.PI)
}

function addPolylineEndpoints(layer: L.Layer): void {
    if (!(layer instanceof L.Polyline) || layer instanceof L.Polygon) return
    const lls = layer.getLatLngs() as L.LatLng[]
    if (!lls || lls.length < 2) return
    const c = (layer.options as L.PolylineOptions).color ?? '#3498db'
    const s = L.marker(lls[0],              { icon: createEndpointIcon('>', segmentAngle(lls[0], lls[1]), c as string, true),  interactive: false })
    const e = L.marker(lls[lls.length - 1], { icon: createEndpointIcon('X', segmentAngle(lls[lls.length-2], lls[lls.length-1]), c as string, false), interactive: false })
    lineEndpointLayer.addLayer(s)
    lineEndpointLayer.addLayer(e)
    ;(layer as any)._endpointMarkers = [s, e]
}

// ─── LABELS ───────────────────────────────────────────────────────────────────

function createPermanentLabel(layer: L.Layer, label: string, phaseKey: string): void {
    if (layer instanceof L.Marker) return
    if (phaseKey === 'areas')     return  // edge label
    if (phaseKey === 'districts') return  // edge label
    ;(layer as L.Path).bindTooltip(label, { permanent: true, direction: 'center', className: 'custom-shape-label' }).openTooltip()
}

// ─── POLYGON EDGE LABELS ──────────────────────────────────────────────────────
// One label per edge, rotated along the edge, size scales with zoom.

function edgeLabelFontSize(): number {
    return Math.max(7, Math.min(18, map.getZoom() * 1.5 - 9))
}

function clearEdgeLabels(layer: L.Layer): void {
    const markers = (layer as any)._edgeLabelMarkers as L.Marker[] | undefined
    if (markers) markers.forEach(m => polygonEdgeLabelLayer.removeLayer(m))
    ;(layer as any)._edgeLabelMarkers = []
}

export function createPolygonEdgeLabel(layer: L.Layer, text: string, color: string): void {
    if (!(layer instanceof L.Polygon)) return
    ;(layer as any)._edgeLabelText  = text
    ;(layer as any)._edgeLabelColor = color
    refreshEdgeLabel(layer)
}

function refreshEdgeLabel(layer: L.Layer): void {
    if (!(layer instanceof L.Polygon)) return
    const text  = (layer as any)._edgeLabelText  as string | undefined
    const color = (layer as any)._edgeLabelColor as string | undefined
    if (!text || !color) return

    clearEdgeLabels(layer)

    const lls = layer.getLatLngs()[0] as L.LatLng[]
    if (!lls?.length) return

    const baseFs   = edgeLabelFontSize()
    const charWidth = 0.6   // em units per character (approx)
    const markers: L.Marker[] = []

    for (let i = 0; i < lls.length; i++) {
        const a  = lls[i], b = lls[(i + 1) % lls.length]
        const pa = map.latLngToLayerPoint(a), pb = map.latLngToLayerPoint(b)
        const dx = pb.x - pa.x, dy = pb.y - pa.y
        const edgePx = Math.sqrt(dx * dx + dy * dy)

        // cap font size so text fits within 85% of the edge — prevents corner overflow
        const maxFs = (edgePx * 0.85) / (text.length * charWidth)
        const fs    = Math.min(baseFs, maxFs)

        // skip if too tiny to be readable
        if (fs < 7) continue

        const mid = L.latLng((a.lat + b.lat) / 2, (a.lng + b.lng) / 2)
        let angle = Math.atan2(dy, dx) * 180 / Math.PI
        if (angle > 90 || angle < -90) angle += 180   // keep text upright

        const html = `<div class="poly-edge-label" style="position:absolute;color:${color};font-size:${fs}px;transform:translate(-50%,-50%) rotate(${angle}deg)">${text}</div>`

        const m = L.marker(mid, {
            icon: L.divIcon({
                className: '',
                html,
                iconSize:   [0, 0],
                iconAnchor: [0, 0],
            }),
            interactive: false,
            zIndexOffset: 200,
        })

        polygonEdgeLabelLayer.addLayer(m)
        markers.push(m)
    }

    ;(layer as any)._edgeLabelMarkers = markers
}

function refreshAllEdgeLabels(): void {
    ;[...featureLayers.areas, ...featureLayers.districts].forEach(({ layer }) => refreshEdgeLabel(layer))
}

// kept for backward-compat with delete handler
export function createAreaPerimeterLabel(layer: L.Layer, areaTypeKey: string): void {
    const at    = AREA_TYPES.find(a => a.key === areaTypeKey) ?? AREA_TYPES[0]
    createPolygonEdgeLabel(layer, 'Urban Perimeter Limit', at.color)
}

// ─── SPATIAL HELPERS ─────────────────────────────────────────────────────────

let municipalLimitRings: L.LatLng[][] = []
let scatteredPolygons:   L.LatLng[][] = []

function pointInRing(latlng: L.LatLng, ring: L.LatLng[]): boolean {
    let inside = false
    const x = latlng.lat, y = latlng.lng
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const xi = ring[i].lat, yi = ring[i].lng, xj = ring[j].lat, yj = ring[j].lng
        if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
            inside = !inside
    }
    return inside
}

function pointInMunicipalLimit(latlng: L.LatLng): boolean {
    if (municipalLimitRings.length === 0) return true
    return municipalLimitRings.some(r => pointInRing(latlng, r))
}

function pointInScatteredArea(latlng: L.LatLng): boolean {
    return scatteredPolygons.some(r => pointInRing(latlng, r))
}

function polylineMidpoint(layer: L.Polyline): L.LatLng {
    const lls = layer.getLatLngs() as L.LatLng[]
    return lls[Math.floor(lls.length / 2)]
}

function extractRings(geom: GeoJSON.Geometry): L.LatLng[][] {
    const rings: L.LatLng[][] = []
    const processRing = (coords: GeoJSON.Position[] | GeoJSON.Position[][] | GeoJSON.Position[][][]): void => {
        if (!coords?.length) return
        if (typeof (coords[0] as GeoJSON.Position)[0] === 'number') {
            rings.push((coords as GeoJSON.Position[]).map(c => L.latLng(c[1], c[0])))
        } else {
            (coords as (GeoJSON.Position[] | GeoJSON.Position[][])[]).forEach(c => processRing(c as any))
        }
    }
    if (geom.type === 'Polygon')         processRing(geom.coordinates)
    else if (geom.type === 'MultiPolygon') geom.coordinates.forEach(p => processRing(p))
    else processRing((geom as any).coordinates)
    return rings
}

// ─── MUNICIPALITY BOUNDARY ────────────────────────────────────────────────────

export async function displayCommuneBoundary(communeId: number, communeName: string): Promise<void> {
    try {
        if (boundariesLayer) { map.removeLayer(boundariesLayer); boundariesLayer = null }
        const res = await apiFetch(`/api/commune/${communeId}/boundary`)
        if (!res.ok) return
        const data = await res.json() as { geometry: string | GeoJSON.Geometry; commune_name?: string }
        const geojson: GeoJSON.Geometry = typeof data.geometry === 'string' ? JSON.parse(data.geometry) : data.geometry
        if (!geojson?.type) return

        municipalLimitRings = extractRings(geojson)
        boundariesLayer = L.geoJSON(geojson, {
            style: { color: '#e74c3c', weight: 2.5, fillOpacity: 0.03, fillColor: '#e74c3c' },
        }).addTo(map)
        map.fitBounds(boundariesLayer.getBounds(), { padding: [50, 50], maxZoom: 14 })
    } catch (e) { console.error('Boundary error:', e) }
}

// ─── SCATTERED AREAS ──────────────────────────────────────────────────────────

function renderScatteredAreas(geoJsonStr: string | GeoJSON.Geometry): void {
    scatteredLayer.clearLayers()
    scatteredPolygons = []
    if (!geoJsonStr) return
    try {
        const geojson: GeoJSON.Geometry = typeof geoJsonStr === 'string' ? JSON.parse(geoJsonStr) : geoJsonStr
        if (!geojson?.type) return
        scatteredPolygons = extractRings(geojson)
        L.geoJSON(geojson, {
            style: scatteredStyle,
            onEachFeature(_, layer) {
                (layer as L.Path).bindTooltip('Scattered Area', { direction: 'center', className: 'boundary-tooltip' })
            },
        }).addTo(scatteredLayer)
    } catch (e) { console.error('Scattered render error:', e) }
}

async function refreshScatteredAreas(): Promise<void> {
    try {
        const res = await apiFetch('/api/areas/refresh-scattered', { method: 'POST' })
        if (!res.ok) return
        const data = await res.json() as ScatteredRefreshResponse
        if (data.geojson) renderScatteredAreas(data.geojson)
        else scatteredLayer.clearLayers()
    } catch (e) { console.error('Scatter refresh error:', e) }
}

// ─── CONTEXT MENU ─────────────────────────────────────────────────────────────

function createContextMenuEl(): HTMLElement {
    const el = document.createElement('div')
    el.id = 'nars-ctx-menu'
    el.className = 'nars-ctx-menu'
    el.style.display = 'none'
    document.body.appendChild(el)

    // Close on any outside click
    document.addEventListener('click', () => { el.style.display = 'none' })
    document.addEventListener('contextmenu', () => { el.style.display = 'none' })
    return el
}

let _ctxEl: HTMLElement | null = null
function getCtxEl(): HTMLElement {
    if (!_ctxEl) _ctxEl = createContextMenuEl()
    return _ctxEl
}

function showContextMenu(x: number, y: number, dbId: number): void {
    const el = getCtxEl()
    el.innerHTML = `
        <div class="nars-ctx-item" data-action="edit">✏️ Edit Info</div>
        <div class="nars-ctx-item" data-action="boundaries">⬟ Edit Boundaries</div>
        <div class="nars-ctx-item nars-ctx-danger" data-action="remove">🗑️ Remove Object</div>
    `
    el.style.display = 'block'

    // Keep menu on screen
    el.style.left = '-9999px'
    el.style.top  = '-9999px'
    el.style.display = 'block'
    const w = el.offsetWidth, h = el.offsetHeight
    el.style.left = (x + w > window.innerWidth  ? x - w : x) + 'px'
    el.style.top  = (y + h > window.innerHeight ? y - h : y) + 'px'

    el.querySelectorAll('.nars-ctx-item').forEach(item => {
        (item as HTMLElement).onclick = (e) => {
            e.stopPropagation()
            el.style.display = 'none'
            const action = (item as HTMLElement).dataset.action
            if      (action === 'edit')       (window as any).__narsEditFeature(dbId)
            else if (action === 'boundaries') (window as any).__narsEditBoundaries(dbId)
            else if (action === 'remove')     (window as any).__narsRemoveFeature(dbId)
        }
    })
}

async function removeFeature(dbId: number): Promise<void> {
    if (!confirm('Remove this feature? This cannot be undone.')) return

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

    const entry = featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    if (!entry) return

    try {
        const res = await apiFetch(`/api/delete/${dbId}`, { method: 'DELETE' })
        if (!res.ok) { alert('Failed to remove feature.'); return }
    } catch (err) { alert('Failed to remove feature.'); return }

    const layer = entry.layer
    drawnItems.removeLayer(layer)

    if ((layer as any)._endpointMarkers)
        (layer as any)._endpointMarkers.forEach((m: L.Layer) => lineEndpointLayer.removeLayer(m))
    if ((layer as any)._perimeterLabel)
        perimeterLabelLayer.removeLayer((layer as any)._perimeterLabel)
    if ((layer as any)._edgeLabelMarkers)
        (layer as any)._edgeLabelMarkers.forEach((m: L.Marker) => polygonEdgeLabelLayer.removeLayer(m))

    featureLayers[phaseKey] = featureLayers[phaseKey].filter((e: LayerEntry) => (e.layer as any)._dbId !== dbId)

    if (phaseKey === 'areas') await refreshScatteredAreas()
    syncCounts()
}

export function bindContextMenu(layer: L.Layer, dbId: number): void {
    layer.on('contextmenu', (e: any) => {
        e.originalEvent.preventDefault()
        e.originalEvent.stopPropagation()
        showContextMenu(e.originalEvent.clientX, e.originalEvent.clientY, dbId)
    })
}

async function editBoundaries(dbId: number): Promise<void> {
    const editBtn = document.querySelector('.leaflet-draw-edit-edit') as HTMLElement | null
    if (!editBtn) {
        alert('Switch to the correct phase first to enable boundary editing.')
        return
    }

    editBtn.click()

    // Find the layer being edited
    const entry = Object.values(featureLayers).flat()
        .find((e: LayerEntry) => (e.layer as any)._dbId === dbId) as LayerEntry | undefined

    // Cancel edit on any click outside the feature
    function onMapClick(e: any) {
        const target = e.originalEvent?.target as HTMLElement | null
        // If click is on the leaflet-draw toolbar or the feature itself, ignore
        if (target?.closest('.leaflet-draw-toolbar') || target?.closest('.leaflet-draw-actions')) return

        // Check if click landed on the feature's layer element
        const layerEl = entry ? (entry.layer as any)._path ?? (entry.layer as any)._icon : null
        if (layerEl && layerEl.contains(target)) return

        // Click was outside — cancel
        const cancelBtn = document.querySelector('.leaflet-draw-actions a[title="Cancel editing, discards all changes"]') as HTMLElement
                       ?? document.querySelector('.leaflet-draw-actions a') as HTMLElement
        if (cancelBtn) cancelBtn.click()

        map.off('click', onMapClick)
        map.off('contextmenu', onMapClick)
    }

    // Small delay so this click doesn't immediately cancel
    setTimeout(() => {
        map.on('click', onMapClick)
        map.on('contextmenu', onMapClick)
    }, 200)
}

;(window as any).__narsEditBoundaries = editBoundaries
;(window as any).__narsEditFeature    = editFeatureInfo
;(window as any).__narsRemoveFeature  = removeFeature

// ─── VERTEX SNAPPING (districts phase) ───────────────────────────────────────

let snapActive  = false
let snapLatLng: L.LatLng | null = null
let snapMarker: L.CircleMarker | null = null

function getSnapRings(): L.LatLng[][] {
    const rings: L.LatLng[][] = []
    ;[...featureLayers.districts, ...featureLayers.areas].forEach(({ layer }) => {
        if (!(layer instanceof L.Polygon)) return
        const ring = (layer.getLatLngs()[0] as L.LatLng[]).filter(ll => ll && ll.lat != null && ll.lng != null)
        if (ring.length >= 2) rings.push(ring)
    })
    if (boundariesLayer) {
        boundariesLayer.eachLayer((bl: L.Layer) => {
            try {
                const lls = (bl as L.Polygon).getLatLngs()
                const flat = (Array.isArray(lls[0]) ? lls[0] : lls) as L.LatLng[]
                const ring = flat.filter(ll => ll && ll.lat != null && ll.lng != null)
                if (ring.length >= 2) rings.push(ring)
            } catch { /* skip */ }
        })
    }
    return rings
}

function closestOnSegment(mp: L.Point, a: L.LatLng, b: L.LatLng): L.LatLng | null {
    try {
        const pa = map.latLngToLayerPoint(a)
        const pb = map.latLngToLayerPoint(b)
        const dx = pb.x - pa.x, dy = pb.y - pa.y
        const lenSq = dx * dx + dy * dy
        if (lenSq === 0) return a
        const t = Math.max(0, Math.min(1, ((mp.x - pa.x) * dx + (mp.y - pa.y) * dy) / lenSq))
        return map.layerPointToLatLng(L.point(pa.x + t * dx, pa.y + t * dy))
    } catch { return null }
}

function pixelDist(mp: L.Point, ll: L.LatLng): number {
    try {
        const p = map.latLngToLayerPoint(ll)
        return Math.hypot(p.x - mp.x, p.y - mp.y)
    } catch { return Infinity }
}

function nearestSnapPoint(mp: L.Point, rings: L.LatLng[][]): { ll: L.LatLng; dist: number } | null {
    let bestVertex: { ll: L.LatLng; dist: number } | null = null
    let bestEdge:   { ll: L.LatLng; dist: number } | null = null

    for (const ring of rings) {
        for (let i = 0; i < ring.length; i++) {
            const a = ring[i], b = ring[(i + 1) % ring.length]
            if (!a || !b) continue

            // Check corner
            const dv = pixelDist(mp, a)
            if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: a, dist: dv }

            // Check edge midpoint
            const cp = closestOnSegment(mp, a, b)
            if (cp) {
                const de = pixelDist(mp, cp)
                if (!bestEdge || de < bestEdge.dist) bestEdge = { ll: cp, dist: de }
            }
        }
    }

    // Corners take priority: if a corner is within snap range, always prefer it
    const CORNER_PX = 40
    const EDGE_PX   = 40
    if (bestVertex && bestVertex.dist <= CORNER_PX) return bestVertex
    if (bestEdge   && bestEdge.dist   <= EDGE_PX)   return bestEdge
    return null
}

// Document-level capture for snap detection — always fires regardless of leaflet-draw
function onSnapMove(e: MouseEvent): void {
    if (!map.getContainer().contains(e.target as Node)) return
    const rect  = map.getContainer().getBoundingClientRect()
    const cp    = L.point(e.clientX - rect.left, e.clientY - rect.top)
    const mp    = map.containerPointToLayerPoint(cp)
    const rings = getSnapRings()

    if (!rings.length) { snapActive = false; snapLatLng = null; return }

    const snap = nearestSnapPoint(mp, rings)
    if (snap) {
        snapLatLng = snap.ll
        snapActive = true
        if (!map.getPane('snapPane')) {
            map.createPane('snapPane')
            map.getPane('snapPane')!.style.zIndex = '9999'
        }
        if (!snapMarker) {
            snapMarker = L.circleMarker(snap.ll, {
                radius: 8, color: '#f39c12', weight: 2.5,
                fillColor: '#fff', fillOpacity: 1,
                interactive: false, pane: 'snapPane',
            } as any).addTo(map)
        } else {
            snapMarker.setLatLng(snap.ll)
            if (!map.hasLayer(snapMarker)) snapMarker.addTo(map)
        }
    } else {
        snapLatLng = null
        snapActive = false
        if (snapMarker && map.hasLayer(snapMarker)) map.removeLayer(snapMarker)
    }
}

function enableSnapping(): void {
    document.addEventListener('mousemove', onSnapMove, true)

    // Patch map.mouseEventToLayerPoint — leaflet-draw calls this internally
    // in BOTH _onMouseMove and _onClick to convert raw DOM events to layer points.
    // This is the single choke point that controls what coordinate gets recorded.
    if (!(map as any)._origMouseEventToLayerPoint) {
        const orig = map.mouseEventToLayerPoint.bind(map)
        ;(map as any)._origMouseEventToLayerPoint = orig
        map.mouseEventToLayerPoint = function(e: MouseEvent): L.Point {
            if (snapActive && snapLatLng) {
                return map.latLngToLayerPoint(snapLatLng)
            }
            return orig(e)
        }
    }
}

function disableSnapping(): void {
    document.removeEventListener('mousemove', onSnapMove, true)
    // Restore original method
    if ((map as any)._origMouseEventToLayerPoint) {
        map.mouseEventToLayerPoint = (map as any)._origMouseEventToLayerPoint
        delete (map as any)._origMouseEventToLayerPoint
    }
    if (snapMarker && map.hasLayer(snapMarker)) map.removeLayer(snapMarker)
    snapActive = false
    snapLatLng = null
}



// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────

function buildDrawControl(phase: typeof PHASES[number]): void {
    if (drawControl) { map.removeControl(drawControl); drawControl = null }

    const opts: L.Control.DrawConstructorOptions = {
        edit: { featureGroup: drawnItems, edit: {}, remove: false },
        draw: {
            polygon:      false,
            polyline:     false,
            rectangle:    false,
            circle:       false,
            circlemarker: false,
            marker:       false,
        },
    }

    if (phase.drawType === 'polygon') {
        opts.draw!.polygon = {
            allowIntersection: false,
            shapeOptions: {
                color:       phase.color,
                weight:      2.5,
                fillOpacity: phase.key === 'areas' ? 0 : 0.15,
                dashArray:   phase.key === 'areas' ? '10, 6' : undefined,
            },
        }
    }
    if (phase.drawType === 'polyline') {
        opts.draw!.polyline = { shapeOptions: { color: phase.color, weight: POLYLINE_WEIGHT } }
    }
    if (phase.drawType === 'marker') {
        const icon = phase.key === 'cityCenter' ? createCityCenterIcon() : createEntranceIcon('?', phase.color)
        opts.draw!.marker = { icon }
    }

    drawControl = new L.Control.Draw(opts)
    map.addControl(drawControl)
}

// ─── PLACEMENT VALIDATION ─────────────────────────────────────────────────────

async function validatePlacement(layer: L.Layer, phase: typeof PHASES[number]): Promise<boolean> {
    let checkPoint: L.LatLng
    if (phase.drawType === 'marker')        checkPoint = (layer as L.Marker).getLatLng()
    else if (phase.drawType === 'polyline') checkPoint = polylineMidpoint(layer as L.Polyline)
    else                                    checkPoint = (layer as L.Polygon).getBounds().getCenter()

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

function buildPopup(data: FeatureData, phase: typeof PHASES[number], dbId?: number): string {
    const lines = [`<b>${data.label}</b>`, `<small>${phase.label}</small>`]
    if (data.decisionNumber)    lines.push(`<small>Decision: ${data.decisionNumber}</small>`)
    if (data.decisionDate)      lines.push(`<small>Date: ${data.decisionDate}</small>`)
    if (data.roadLabel)         lines.push(`<small>Road: ${data.roadLabel}</small>`)
    if (data.side)              lines.push(`<small>Side: ${data.side} (${data.side === 'left' ? 'odd' : 'even'})</small>`)
    if (data.mainEntranceLabel) lines.push(`<small>Main entrance: ${data.mainEntranceLabel}</small>`)
    return lines.join('<br>')
}

// ── Global handler called by right-click menu ─────────────────────────────────

async function editFeatureInfo(dbId: number): Promise<void> {
    map.closePopup()

    const phaseKey = Object.keys(featureLayers).find(k =>
        featureLayers[k].some((e: LayerEntry) => (e.layer as any)._dbId === dbId))
    if (!phaseKey) return

    const entry = featureLayers[phaseKey].find((e: LayerEntry) => (e.layer as any)._dbId === dbId)
    if (!entry) return

    const phase      = PHASES.find(p => p.key === phaseKey)
    const phaseIndex = PHASES.findIndex(p => p.key === phaseKey)
    if (!phase || phaseIndex === -1) return

    const result = await openEditModal(phaseIndex, dbId, entry.data)
    if (!result) return

    // Merge result into entry.data
    Object.assign(entry.data, result)

    // Persist to DB
    try {
        await apiFetch(`/api/update/${dbId}`, {
            method:  'PUT',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ data: entry.data }),
        })
    } catch (err) { console.error('Edit info save error:', err) }

    // Refresh popup
    ;(entry.layer as L.Path).bindPopup(buildPopup(entry.data, phase, dbId))

    // Refresh style + edge label for areas/districts
    if (phaseKey === 'areas') {
        ;(entry.layer as L.Path).setStyle(areaStyle(entry.data.areaTypeKey ?? 'central_urban'))
        createAreaPerimeterLabel(entry.layer, entry.data.areaTypeKey ?? 'central_urban')
    }
    if (phaseKey === 'districts') {
        createPolygonEdgeLabel(entry.layer, entry.data.label, '#f39c12')
    }
}

;(window as any).__narsEditFeature = editFeatureInfo

// ─── FEATURE DATA + API SAVE ──────────────────────────────────────────────────

function buildFeatureData(layer: L.Layer, phase: typeof PHASES[number], modalResult: Record<string, unknown>): FeatureData {
    const base: FeatureData = {
        type:           phase.key,
        label:          modalResult.label as string,
        decisionNumber: modalResult.decisionNumber as string,
        decisionDate:   modalResult.decisionDate as string,
        ...modalResult as Partial<FeatureData>,
    }
    if (phase.drawType === 'marker') {
        const ll = (layer as L.Marker).getLatLng()
        return { ...base, lat: ll.lat, lng: ll.lng }
    }
    const lls = phase.drawType === 'polygon'
        ? ((layer as L.Polygon).getLatLngs()[0] as L.LatLng[])
        : ((layer as L.Polyline).getLatLngs() as L.LatLng[])

    // PostGIS/GEOS requires closed rings (first === last point exactly).
    // Snapping can produce rings where they differ by floating point — close it.
    let coords = lls.map(ll => ({ lat: ll.lat, lng: ll.lng }))
    if (phase.drawType === 'polygon' && coords.length >= 3) {
        const first = coords[0], last = coords[coords.length - 1]
        if (first.lat !== last.lat || first.lng !== last.lng) {
            coords = [...coords, { lat: first.lat, lng: first.lng }]
        }
    }
    return { ...base, coordinates: coords }
}

function toApiSaveShape(fd: FeatureData): { type: string; layer: string } | null {
    switch (fd.type) {
        case 'areas':              return { type: 'area',            layer: fd.areaTypeKey     ?? 'central_urban' }
        case 'cityCenter':         return { type: 'city_center',     layer: 'city_center' }
        case 'districts':          return { type: 'district',        layer: fd.districtTypeKey ?? 'district' }
        case 'roads':              return { type: 'road',            layer: fd.roadTypeKey     ?? 'street' }
        case 'mainEntrances':      return { type: 'house_entrance',  layer: 'main_entrance' }
        case 'secondaryEntrances': return { type: 'house_entrance',  layer: 'secondary_entrance' }
        case 'publicBuildings':    return { type: 'public_building', layer: 'public_building' }
        case 'publicSpaces':       return { type: 'public_space',    layer: fd.spaceTypeKey    ?? 'garden' }
        default: return null
    }
}

async function saveToDatabase(featureData: FeatureData): Promise<SaveResult> {
    try {
        const shape = toApiSaveShape(featureData)
        if (!shape) return { ok: false, error: `Unknown type '${featureData.type}'.` }

        const res = await apiFetch('/api/save', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ type: shape.type, layer: shape.layer, label: featureData.label, data: featureData }),
        })
        if (!res.ok) {
            const raw = await res.text()
            let detail = raw || `HTTP ${res.status}`
            try { const p = JSON.parse(raw) as { detail?: string; title?: string }; detail = p?.detail ?? p?.title ?? detail } catch { /* ignore */ }
            return { ok: false, error: `HTTP ${res.status}: ${String(detail).slice(0, 240)}` }
        }
        return { ok: true, data: await res.json() as { id: number } }
    } catch (err) {
        return { ok: false, error: (err as Error)?.message ?? 'Network error' }
    }
}

// ─── LOAD FROM DATABASE ───────────────────────────────────────────────────────

export async function loadFromDatabase(): Promise<void> {
    try {
        const res = await apiFetch('/api/load')
        if (!res.ok) { console.error('Load failed:', res.status); return }
        const features = await res.json() as DbFeature[]
        if (!features.length) { console.log('No saved features.'); return }

        drawnItems.clearLayers()
        lineEndpointLayer.clearLayers()
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
                    const icon = phase.key === 'cityCenter' ? createCityCenterIcon() : createEntranceIcon(data.label, phase.color)
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
                drawnItems.addLayer(layer)
                bindContextMenu(layer, feature.id)
                if (phase.drawType === 'polyline') addPolylineEndpoints(layer)
                createPermanentLabel(layer, data.label, phaseKey)
                if (phaseKey === 'areas')     createAreaPerimeterLabel(layer, data.areaTypeKey ?? feature.layer)
                if (phaseKey === 'districts') createPolygonEdgeLabel(layer, data.label, '#f39c12')
                ;(layer as L.Path).bindPopup(buildPopup(data, phase, feature.id))

                featureLayers[phaseKey].push({ layer, data })
                loaded++
            } catch (err) { console.error('Load feature error:', err); skipped++ }
        }

        for (let i = PHASES.length - 1; i >= 0; i--) {
            if (featureLayers[PHASES[i].key].length > 0) { store.currentPhase = i; break }
        }
        if (store.currentPhase >= 2 && store.cityCenterMode === null) store.cityCenterMode = 'auto'

        buildDrawControl(PHASES[store.currentPhase])
        syncCounts()
        console.log(`✓ Loaded ${loaded} features (${skipped} skipped)`)
    } catch (err) { console.error('Load error:', err) }
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
        if (from.key === 'roads'        && featureLayers.roads.length === 0)           { alert('Please draw at least one road before proceeding.'); return }
        if (from.key === 'mainEntrances'&& featureLayers.mainEntrances.length === 0)   { alert('Please place at least one main entrance before proceeding.'); return }
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
    // Enable snapping immediately when entering districts phase
    if (PHASES[index].key === 'districts') enableSnapping()
    else disableSnapping()
}

export function cityCenterYes(): void {
    store.cityCenterDialogVisible = false
}

export function cityCenterSkip(): void {
    store.cityCenterDialogVisible = false
    store.cityCenterMode = 'auto'
    setPhase(2)
}

// ─── DRAW EVENTS ─────────────────────────────────────────────────────────────

function registerDrawEvents(): void {

    map.on(L.Draw.Event.DRAWSTART, () => {
        if (PHASES[store.currentPhase]?.key === 'districts') enableSnapping()
    })
    map.on(L.Draw.Event.DRAWSTOP, () => disableSnapping())

    map.on(L.Draw.Event.CREATED, async (event: L.LeafletEvent) => {
        const e     = event as any
        const layer = e.layer as L.Layer
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
        drawnItems.addLayer(layer)
        bindContextMenu(layer, saveResult.data!.id)
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
            setTimeout(() => setPhase(2), 400)
        }

        if (phase.key === 'areas') await refreshScatteredAreas()

        syncCounts()
    })

    map.on(L.Draw.Event.EDITED, async (event: L.LeafletEvent) => {
        const e = event as any
        e.layers.eachLayer(async (layer: L.Layer) => {
            if (!(layer as any)._dbId) return
            try {
                const phase = PHASES.find(p => featureLayers[p.key].some((f: LayerEntry) => f.layer === layer))
                const entry = phase ? featureLayers[phase.key].find((f: LayerEntry) => f.layer === layer) : null
                if (!entry) return

                if (layer instanceof L.Marker) {
                    const ll = layer.getLatLng()
                    entry.data.lat = ll.lat
                    entry.data.lng = ll.lng
                } else if (layer instanceof L.Polyline && !(layer instanceof L.Polygon)) {
                    entry.data.coordinates = (layer.getLatLngs() as L.LatLng[]).map(ll => ({ lat: ll.lat, lng: ll.lng }))
                } else if (layer instanceof L.Polygon) {
                    entry.data.coordinates = ((layer.getLatLngs()[0]) as L.LatLng[]).map(ll => ({ lat: ll.lat, lng: ll.lng }))
                }

                await apiFetch(`/api/update/${(layer as any)._dbId}`, {
                    method:  'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body:    JSON.stringify({ data: entry.data }),
                })

                // Refresh popup so Edit Info button stays present
                if (phase) {
                    ;(layer as L.Path).bindPopup(buildPopup(entry.data, phase, (layer as any)._dbId))
                }

                if (phase?.key === 'areas') {
                    createAreaPerimeterLabel(layer, entry.data.areaTypeKey ?? 'central_urban')
                    await refreshScatteredAreas()
                }
                if (phase?.key === 'districts') {
                    createPolygonEdgeLabel(layer, entry.data.label, '#f39c12')
                }
            } catch (err) { console.error('Edit persist error:', err) }
        })

        lineEndpointLayer.clearLayers()
        drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l) })
    })

    map.on(L.Draw.Event.DELETED, async (event: L.LeafletEvent) => {
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
            if ((layer as any)._endpointMarkers) (layer as any)._endpointMarkers.forEach((m: L.Layer) => lineEndpointLayer.removeLayer(m))
            if ((layer as any)._perimeterLabel)  perimeterLabelLayer.removeLayer((layer as any)._perimeterLabel)
            if ((layer as any)._edgeLabelMarkers) {
                ;(layer as any)._edgeLabelMarkers.forEach((m: L.Marker) => polygonEdgeLabelLayer.removeLayer(m))
            }
        })

        lineEndpointLayer.clearLayers()
        drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l) })

        for (const key of Object.keys(featureLayers))
            featureLayers[key] = featureLayers[key].filter((f: LayerEntry) => drawnItems.hasLayer(f.layer))

        if (areaDeleted) await refreshScatteredAreas()
        syncCounts()
    })
}

// ─── MODAL EXTRA PREPARATION ──────────────────────────────────────────────────

async function prepareModalExtras(phase: typeof PHASES[number], layer: L.Layer): Promise<void> {
    const m = store.modal

    if (phase.key === 'areas') {
        m.mainUrbanExists = await checkMainUrbanExists()
        if (!m.mainUrbanExists && store.municipalityName) m.label = store.municipalityName
        m.areaTypeKey = m.mainUrbanExists ? 'secondary_urban' : 'central_urban'
    }

    if (phase.key === 'mainEntrances') {
        m.roadOptions = featureLayers.roads.map((r, i) => ({
            idx:   i,
            label: r.data.label || `Road ${i + 1}`,
            dbId:  (r.layer as any)._dbId as number,
        }))
    }

    if (phase.key === 'secondaryEntrances') {
        m.mainEntranceOptions = featureLayers.mainEntrances.map((e, i) => ({
            idx:   i,
            label: e.data.label || `Entrance ${i + 1}`,
            dbId:  (e.layer as any)._dbId as number,
        }))
    }
}

export async function fetchRoadSide(roadDbId: number, _roadIdx: number): Promise<void> {
    const m = store.modal
    m.entranceSideLoading = true
    m.entranceSide        = null
    m.entranceNumber      = null

    try {
        const ll = currentModalLayer ? (currentModalLayer as L.Marker).getLatLng() : null
        if (!ll) return
        const result = await getRoadSide(roadDbId, ll.lat, ll.lng)
        if (result) {
            m.entranceSide   = result.side
            m.entranceNumber = result.suggestedNumber
        }
    } finally {
        m.entranceSideLoading = false
    }
}

export function computeBisNumber(mainEntranceDbId: number): void {
    const count = featureLayers.secondaryEntrances.filter((s: LayerEntry) =>
        s.data.mainEntranceDbId === mainEntranceDbId).length
    store.modal.bisNumber = count + 1
    store.modal.label     = 'BIS' + String(count + 1).padStart(2, '0')
}

// ─── STYLE APPLICATOR ────────────────────────────────────────────────────────

function applyStyle(layer: L.Layer, phase: typeof PHASES[number], modalResult: FeatureData): void {
    if      (phase.key === 'areas')              (layer as L.Path).setStyle(areaStyle(modalResult.areaTypeKey ?? 'central_urban'))
    else if (phase.key === 'districts')          (layer as L.Path).setStyle(polygonStyles.districts)
    else if (phase.key === 'publicBuildings')    (layer as L.Path).setStyle(polygonStyles.publicBuildings)
    else if (phase.key === 'publicSpaces')       (layer as L.Path).setStyle(polygonStyles.publicSpaces)
    else if (phase.drawType === 'polyline')      (layer as L.Path).setStyle({ color: phase.color, weight: POLYLINE_WEIGHT })
    else if (phase.key === 'mainEntrances')
        (layer as L.Marker).setIcon(createEntranceIcon(String(modalResult.entranceNumber ?? modalResult.label), phase.color))
    else if (phase.key === 'secondaryEntrances') {
        const bisStr = 'BIS' + String(modalResult.bisNumber ?? 1).padStart(2, '0')
        ;(layer as L.Marker).setIcon(createEntranceIcon(bisStr, phase.color))
    }
    else if (phase.key === 'cityCenter')
        (layer as L.Marker).setIcon(createCityCenterIcon())
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
