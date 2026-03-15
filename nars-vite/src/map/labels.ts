// ─── LABELS & LAYER VISIBILITY ───────────────────────────────────────────────

import { ctx }             from './state'
import { AREA_TYPES, PHASES } from '../phases'
import { featureLayers, store } from '../store'
import { areaStyle }        from './styles'
import type { LayerEntry }  from '../types'

declare const L: typeof import('leaflet')

// ─── POLYLINE ENDPOINT MARKERS ────────────────────────────────────────────────

function segmentAngle(a: L.LatLng, b: L.LatLng): number {
    const fp = ctx.map.latLngToLayerPoint(a), tp = ctx.map.latLngToLayerPoint(b)
    return Math.atan2(tp.y - fp.y, tp.x - fp.x) * (180 / Math.PI)
}

import { createEndpointIcon } from './styles'

export function addPolylineEndpoints(layer: L.Layer): void {
    if (!(layer instanceof L.Polyline) || layer instanceof L.Polygon) return
    const lls = layer.getLatLngs() as L.LatLng[]
    if (!lls || lls.length < 2) return
    const c = (layer.options as L.PolylineOptions).color ?? '#3498db'

    // Suppress the start '>' arrow if it sits on top of a city center marker
    // (road starts at the city center — the CC icon already marks that point).
    const startLL  = lls[0]
    const ccMarkers: L.LatLng[] = ((window as any).__narsGetCityCenterLatLngs?.() ?? [])
    const onCityCenter = ccMarkers.some(cc => startLL.distanceTo(cc) < 2)

    const markers: L.Marker[] = []
    if (!onCityCenter) {
        const s = L.marker(startLL, { icon: createEndpointIcon('>', segmentAngle(lls[0], lls[1]), c as string, true), interactive: false })
        ;(s as any).pm?.setOptions?.({ pmIgnore: true })
        ctx.lineEndpointLayer.addLayer(s)
        markers.push(s)
    }
    const e = L.marker(lls[lls.length - 1], { icon: createEndpointIcon('X', segmentAngle(lls[lls.length-2], lls[lls.length-1]), c as string, false), interactive: false })
    ;(e as any).pm?.setOptions?.({ pmIgnore: true })
    ctx.lineEndpointLayer.addLayer(e)
    markers.push(e)
    ;(layer as any)._endpointMarkers = markers
}

// ─── PERMANENT LABELS ─────────────────────────────────────────────────────────

export function createPermanentLabel(layer: L.Layer, label: string, phaseKey: string): void {
    if (layer instanceof L.Marker) return
    if (phaseKey === 'areas')     return  // edge label
    if (phaseKey === 'districts') return  // edge label
    ;(layer as L.Path).bindTooltip(label, { permanent: true, direction: 'center', className: 'custom-shape-label' }).openTooltip()
}

// ─── POLYGON EDGE LABELS ──────────────────────────────────────────────────────
// One label per edge, rotated along the edge, size scales with zoom.

function edgeLabelFontSize(): number {
    return Math.max(7, Math.min(18, ctx.map.getZoom() * 1.5 - 9))
}

function clearEdgeLabels(layer: L.Layer): void {
    const markers = (layer as any)._edgeLabelMarkers as L.Marker[] | undefined
    if (markers) markers.forEach(m => ctx.polygonEdgeLabelLayer.removeLayer(m))
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

    const baseFs    = edgeLabelFontSize()
    const charWidth = 0.6
    const markers: L.Marker[] = []

    for (let i = 0; i < lls.length; i++) {
        const a  = lls[i], b = lls[(i + 1) % lls.length]
        const pa = ctx.map.latLngToLayerPoint(a), pb = ctx.map.latLngToLayerPoint(b)
        const dx = pb.x - pa.x, dy = pb.y - pa.y
        const edgePx = Math.sqrt(dx * dx + dy * dy)

        const maxFs = (edgePx * 0.85) / (text.length * charWidth)
        const fs    = Math.min(baseFs, maxFs)
        if (fs < 7) continue

        const mid = L.latLng((a.lat + b.lat) / 2, (a.lng + b.lng) / 2)
        let angle = Math.atan2(dy, dx) * 180 / Math.PI
        if (angle > 90 || angle < -90) angle += 180

        const html = `<div class="poly-edge-label" style="position:absolute;color:${color};font-size:${fs}px;transform:translate(-50%,-50%) rotate(${angle}deg)">${text}</div>`

        const m = L.marker(mid, {
            icon: L.divIcon({ className: '', html, iconSize: [0, 0], iconAnchor: [0, 0] }),
            interactive: false,
            zIndexOffset: 200,
        })
        ;(m as any).pm?.setOptions?.({ pmIgnore: true })

        ctx.polygonEdgeLabelLayer.addLayer(m)
        markers.push(m)
    }

    ;(layer as any)._edgeLabelMarkers = markers
}

export function refreshAllEdgeLabels(): void {
    ;[...featureLayers.areas, ...featureLayers.districts].forEach(({ layer }) => refreshEdgeLabel(layer))
    // Newly created markers start visible — re-apply current phase visibility
    refreshLayerVisibility()
}

// kept for backward-compat with delete handler
export function createAreaPerimeterLabel(layer: L.Layer, areaTypeKey: string): void {
    const at = AREA_TYPES.find(a => a.key === areaTypeKey) ?? AREA_TYPES[0]
    createPolygonEdgeLabel(layer, 'Urban Perimeter Limit', at.color)
}

// ─── LAYER VISIBILITY ─────────────────────────────────────────────────────────
// ── Visibility lookup table — derived directly from Phases.xlsx ───────────────
// Each entry lists the layer keys that should be visible for that phase.
// The current phase's own layer is always visible (added at runtime below).
// 'roads' visibility is handled separately via roadsDisplayLayer.

const PHASE_VISIBILITY: Record<string, ReadonlySet<string>> = {
    areas:           new Set(['areas']),
    districts:       new Set(['areas', 'districts']),
    cityCenter:      new Set(['areas', 'cityCenter']),
    roads:           new Set(['areas', 'cityCenter', 'roads']),
    houseEntrances:  new Set(['areas', 'cityCenter', 'roads', 'houseEntrances']),
    publicBuildings: new Set(['areas', 'publicBuildings']),
    publicSpaces:    new Set(['areas', 'publicSpaces']),
    namingPanels:    new Set(['areas', 'districts', 'roads', 'publicBuildings', 'publicSpaces', 'namingPanels']),
}

export function refreshLayerVisibility(): void {
    const currentKey = PHASES[store.currentPhase]?.key
    const visible    = PHASE_VISIBILITY[currentKey] ?? new Set([currentKey])

    for (const [key, entries] of Object.entries(featureLayers)) {
        const isRoads = key === 'roads'
        const show    = visible.has(key)

        // Roads stay interactive during houseEntrances for right-click menus.
        // Areas are never interactive outside their own phase.
        // In namingPanels phase all non-current layers are display-only.
        const isRoadOverlay = isRoads && currentKey === 'houseEntrances'
        let interactive = isRoadOverlay || (key !== 'areas') || (currentKey === 'areas')
        if (currentKey === 'namingPanels' && key !== 'namingPanels') interactive = false

        for (const { layer } of entries as LayerEntry[]) {
            // Roads live permanently in roadsDisplayLayer — never in drawnItems.
            if (!isRoads) {
                if (show) {
                    if (!ctx.drawnItems.hasLayer(layer)) ctx.drawnItems.addLayer(layer)
                    // namingPanels: overlay layers render above roads visually.
                    if (currentKey === 'namingPanels' && key !== 'namingPanels')
                        if (layer instanceof L.Path) (layer as L.Path).bringToFront()
                } else {
                    if (ctx.drawnItems.hasLayer(layer)) ctx.drawnItems.removeLayer(layer)
                }
            } else {
                // Roads: push behind overlays during namingPanels.
                if (currentKey === 'namingPanels' && layer instanceof L.Path)
                    (layer as L.Path).bringToBack()
            }

            if (layer instanceof L.Path) {
                const el = (layer as any).getElement?.() as SVGElement | undefined
                if (el) el.style.pointerEvents = interactive ? '' : 'none'
            }

            const tooltip = (layer as any).getTooltip?.()
            if (tooltip) { show ? (layer as any).openTooltip?.() : (layer as any).closeTooltip?.() }

            const edgeMarkers = (layer as any)._edgeLabelMarkers as L.Marker[] | undefined
            edgeMarkers?.forEach(m => {
                const el = (m as any)._icon as HTMLElement | undefined
                if (el) el.style.display = show ? '' : 'none'
            })
            const perimLabel = (layer as any)._perimeterLabel as L.Layer | undefined
            if (perimLabel) {
                const pl = perimLabel as any
                if (pl._icon) pl._icon.style.display = show ? '' : 'none'
            }
        }
    }

    // Endpoint arrows — shown when roads are visible and operator needs direction reference.
    const showArrows = visible.has('roads')
    if (showArrows) {
        if (!ctx.map.hasLayer(ctx.lineEndpointLayer)) ctx.map.addLayer(ctx.lineEndpointLayer)
    } else {
        if (ctx.map.hasLayer(ctx.lineEndpointLayer)) ctx.map.removeLayer(ctx.lineEndpointLayer)
    }

    // Roads layer group — driven by the same visibility table.
    const showRoads = visible.has('roads')
    if (showRoads) {
        if (!ctx.map.hasLayer(ctx.roadsDisplayLayer)) ctx.map.addLayer(ctx.roadsDisplayLayer)
    } else {
        if (ctx.map.hasLayer(ctx.roadsDisplayLayer)) ctx.map.removeLayer(ctx.roadsDisplayLayer)
    }
}
