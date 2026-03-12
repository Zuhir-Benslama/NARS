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
// Always show areas. Show the current phase's layers. Hide everything else.

function setLayerVisible(layer: L.Layer, visible: boolean): void {
    if (layer instanceof L.Marker) {
        const el = (layer as any)._icon as HTMLElement | undefined
        if (el) el.style.display = visible ? '' : 'none'
    } else if (layer instanceof L.Path) {
        ;(layer as any)._path?.style.setProperty('display', visible ? '' : 'none')
    }
    // Permanent tooltip (road names, building names, etc.)
    const tooltip = (layer as any).getTooltip?.()
    if (tooltip) {
        if (visible) (layer as any).openTooltip?.()
        else         (layer as any).closeTooltip?.()
    }
}

export function refreshLayerVisibility(): void {
    const currentKey = PHASES[store.currentPhase]?.key

    for (const [key, entries] of Object.entries(featureLayers)) {
        const showCityCenter = key === 'cityCenter' && (currentKey === 'roads' || currentKey === 'houseEntrances')
        const show = key === 'areas' || key === currentKey || showCityCenter
        // Areas are always visible but must not intercept pointer events when
        // another phase is active — otherwise right-click on a district/road
        // beneath an area polygon fires on the area instead of the intended layer.
        const interactive = key !== 'areas' || currentKey === 'areas'
        for (const { layer } of entries as LayerEntry[]) {
            setLayerVisible(layer, show)
            if (layer instanceof L.Path) {
                const el = (layer as any).getElement?.() as SVGElement | undefined
                if (el) el.style.pointerEvents = interactive ? '' : 'none'
            }
            const edgeMarkers = (layer as any)._edgeLabelMarkers as L.Marker[] | undefined
            edgeMarkers?.forEach(m => setLayerVisible(m, show))
            const perimLabel = (layer as any)._perimeterLabel as L.Layer | undefined
            if (perimLabel) setLayerVisible(perimLabel, show)
        }
    }

    // Road endpoint markers are tied to the roads phase
    ctx.lineEndpointLayer.eachLayer(l => setLayerVisible(l, currentKey === 'roads'))
}
