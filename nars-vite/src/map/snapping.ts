// ─── VERTEX SNAPPING (districts + roads phases) ───────────────────────────────

import { ctx }          from './state'
import { featureLayers } from '../store'
import type { LayerEntry } from '../types'

declare const L: typeof import('leaflet')

let snapActive:     boolean                      = false
let snapLatLng:     L.LatLng | null              = null
let snapMarker:     L.CircleMarker | null        = null
let activeSnapMode: 'districts' | 'roads' | null = null
let snapExclude:    L.Layer | null               = null
let snapPhaseKey:   string | null                = null

// ─── SNAP SOURCE COLLECTORS ───────────────────────────────────────────────────

function getSnapRings(phaseKey?: string): L.LatLng[][] {
    const rings: L.LatLng[][] = []
    ;[...featureLayers.areas, ...(phaseKey === 'areas' ? [] : featureLayers.districts)].forEach(({ layer }) => {
        if (layer === snapExclude) return
        if (!(layer instanceof L.Polygon)) return
        const ring = (layer.getLatLngs()[0] as L.LatLng[]).filter(ll => ll && ll.lat != null && ll.lng != null)
        if (ring.length >= 2) rings.push(ring)
    })
    if (ctx.boundariesLayer) {
        ctx.boundariesLayer.eachLayer((bl: L.Layer) => {
            try {
                const lls = (bl as L.Polygon).getLatLngs()
                const extractRings = (arr: any): void => {
                    if (!arr.length) return
                    if (arr[0] instanceof L.LatLng) {
                        const r = (arr as L.LatLng[]).filter(ll => ll?.lat != null && ll?.lng != null)
                        if (r.length >= 2) rings.push(r)
                    } else {
                        arr.forEach(extractRings)
                    }
                }
                extractRings(lls)
            } catch { /* skip */ }
        })
    }
    return rings
}

function getRoadSnapSources(): { chains: L.LatLng[][]; rings: L.LatLng[][]; points: L.LatLng[]; circles: L.Circle[] } {
    const chains:   L.LatLng[][] = []
    const rings:    L.LatLng[][] = []
    const points:   L.LatLng[]   = []
    const circles:  L.Circle[]   = []

    featureLayers.roads.forEach(({ layer }) => {
        if (layer === snapExclude) return
        if (!(layer instanceof L.Polyline) || layer instanceof L.Polygon) return
        const lls = (layer.getLatLngs() as L.LatLng[]).filter(ll => ll?.lat != null && ll?.lng != null)
        if (lls.length >= 2) chains.push(lls)
    })

    featureLayers.areas.forEach(({ layer }) => {
        if (!(layer instanceof L.Polygon)) return
        const ring = (layer.getLatLngs()[0] as L.LatLng[]).filter(ll => ll?.lat != null && ll?.lng != null)
        if (ring.length >= 2) rings.push(ring)
    })

    featureLayers.cityCenter.forEach(({ layer }) => {
        if (layer instanceof L.Circle) circles.push(layer as L.Circle)
    })

    return { chains, rings, points, circles }
}

// ─── GEOMETRY HELPERS ────────────────────────────────────────────────────────

function closestOnSegment(mp: L.Point, a: L.LatLng, b: L.LatLng): L.LatLng | null {
    try {
        const pa = ctx.map.latLngToLayerPoint(a)
        const pb = ctx.map.latLngToLayerPoint(b)
        const dx = pb.x - pa.x, dy = pb.y - pa.y
        const lenSq = dx * dx + dy * dy
        if (lenSq === 0) return a
        const t = Math.max(0, Math.min(1, ((mp.x - pa.x) * dx + (mp.y - pa.y) * dy) / lenSq))
        return ctx.map.layerPointToLatLng(L.point(pa.x + t * dx, pa.y + t * dy))
    } catch { return null }
}

// Returns the closest point on a circle's visual perimeter (in pixel space)
// and its pixel distance from mp. Returns null if the circle has zero radius.
function closestOnCirclePerimeter(
    mp: L.Point,
    circle: L.Circle,
): { ll: L.LatLng; dist: number } | null {
    try {
        const c = circle as any
        const centerPx: L.Point = c._point
        const radiusPx: number  = c._radius
        if (!centerPx || !radiusPx || radiusPx === 0) return null
        const dx = mp.x - centerPx.x
        const dy = mp.y - centerPx.y
        const cursorDist = Math.hypot(dx, dy)
        if (cursorDist === 0) return null
        const snapPx = L.point(
            centerPx.x + (dx / cursorDist) * radiusPx,
            centerPx.y + (dy / cursorDist) * radiusPx,
        )
        const snapLL = ctx.map.layerPointToLatLng(snapPx)
        const dist   = Math.abs(cursorDist - radiusPx)
        return { ll: snapLL, dist }
    } catch { return null }
}

function pixelDist(mp: L.Point, ll: L.LatLng): number {
    try {
        const p = ctx.map.latLngToLayerPoint(ll)
        return Math.hypot(p.x - mp.x, p.y - mp.y)
    } catch { return Infinity }
}

// ─── NEAREST SNAP POINT ───────────────────────────────────────────────────────

function nearestSnapPoint(mp: L.Point, rings: L.LatLng[][]): { ll: L.LatLng; dist: number } | null {
    let bestVertex: { ll: L.LatLng; dist: number } | null = null
    let bestEdge:   { ll: L.LatLng; dist: number } | null = null

    for (const ring of rings) {
        for (let i = 0; i < ring.length; i++) {
            const a = ring[i], b = ring[(i + 1) % ring.length]
            if (!a || !b) continue
            const dv = pixelDist(mp, a)
            if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: a, dist: dv }
            const cp = closestOnSegment(mp, a, b)
            if (cp) {
                const de = pixelDist(mp, cp)
                if (!bestEdge || de < bestEdge.dist) bestEdge = { ll: cp, dist: de }
            }
        }
    }

    const CORNER_PX = 40, EDGE_PX = 40
    if (bestVertex && bestVertex.dist <= CORNER_PX) return bestVertex
    if (bestEdge   && bestEdge.dist   <= EDGE_PX)   return bestEdge
    return null
}

function nearestSnapPointRoads(
    mp:      L.Point,
    chains:  L.LatLng[][],
    rings:   L.LatLng[][],
    points:  L.LatLng[],
    circles: L.Circle[],
): { ll: L.LatLng; dist: number } | null {
    let bestVertex: { ll: L.LatLng; dist: number } | null = null
    let bestEdge:   { ll: L.LatLng; dist: number } | null = null

    for (const pt of points) {
        if (!pt) continue
        const dv = pixelDist(mp, pt)
        if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: pt, dist: dv }
    }

    for (const chain of chains) {
        for (let i = 0; i < chain.length; i++) {
            const a = chain[i]
            if (!a) continue
            const dv = pixelDist(mp, a)
            if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: a, dist: dv }
            if (i < chain.length - 1) {
                const b = chain[i + 1]
                if (!b) continue
                const cp = closestOnSegment(mp, a, b)
                if (cp) {
                    const de = pixelDist(mp, cp)
                    if (!bestEdge || de < bestEdge.dist) bestEdge = { ll: cp, dist: de }
                }
            }
        }
    }

    for (const ring of rings) {
        for (let i = 0; i < ring.length; i++) {
            const a = ring[i], b = ring[(i + 1) % ring.length]
            if (!a || !b) continue
            const dv = pixelDist(mp, a)
            if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: a, dist: dv }
            const cp = closestOnSegment(mp, a, b)
            if (cp) {
                const de = pixelDist(mp, cp)
                if (!bestEdge || de < bestEdge.dist) bestEdge = { ll: cp, dist: de }
            }
        }
    }

    // Snap to city center circle perimeters — wins over vertex/edge if closer
    let bestCircle: { ll: L.LatLng; dist: number } | null = null
    for (const circle of circles) {
        const result = closestOnCirclePerimeter(mp, circle)
        if (result && (!bestCircle || result.dist < bestCircle.dist))
            bestCircle = result
    }

    const CORNER_PX = 40, EDGE_PX = 40, CIRCLE_PX = 20
    if (bestCircle  && bestCircle.dist  <= CIRCLE_PX) return bestCircle
    if (bestVertex  && bestVertex.dist  <= CORNER_PX) return bestVertex
    if (bestEdge    && bestEdge.dist    <= EDGE_PX)   return bestEdge
    return null
}


let snapFrozen = false  // true from mousedown until after click

function onSnapMouseDown(): void {
    if (!editModeActive && snapActive && snapLatLng) {
        snapFrozen = true
    }
}

function onSnapMouseUp(): void {
    snapFrozen = false
}

function onSnapMove(e: MouseEvent): void {
    if (snapFrozen) return
    if (!ctx.map.getContainer().contains(e.target as Node)) return

    if (editModeActive && !editDragActive) {
        snapActive = false
        snapLatLng = null
        if (snapMarker && ctx.map.hasLayer(snapMarker)) ctx.map.removeLayer(snapMarker)
        return
    }

    const rect = ctx.map.getContainer().getBoundingClientRect()
    const cp   = L.point(e.clientX - rect.left, e.clientY - rect.top)
    const mp   = ctx.map.containerPointToLayerPoint(cp)
    let snap: { ll: L.LatLng; dist: number } | null = null
    let snapColor = '#f39c12'

    if (activeSnapMode === 'districts') {
        const rings = getSnapRings(snapPhaseKey ?? undefined)
        if (!rings.length) { snapActive = false; snapLatLng = null; return }
        snap = nearestSnapPoint(mp, rings)
    } else if (activeSnapMode === 'roads') {
        const { chains, rings, points, circles } = getRoadSnapSources()
        if (!chains.length && !rings.length && !points.length && !circles.length) { snapActive = false; snapLatLng = null; return }
        snap = nearestSnapPointRoads(mp, chains, rings, points, circles)
        snapColor = '#3498db'
    } else {
        snapActive = false; snapLatLng = null; return
    }

    if (snap) {
        snapLatLng = snap.ll
        snapActive = true

        if (!ctx.map.getPane('snapPane')) {
            ctx.map.createPane('snapPane')
            ctx.map.getPane('snapPane')!.style.zIndex = '9999'
        }
        if (!snapMarker) {
            snapMarker = L.circleMarker(snap.ll, {
                radius: 8, color: snapColor, weight: 2.5,
                fillColor: '#fff', fillOpacity: 1,
                interactive: false, pane: 'snapPane',
            } as any).addTo(ctx.map)
        } else {
            snapMarker.setLatLng(snap.ll)
            snapMarker.setStyle({ color: snapColor })
            if (!ctx.map.hasLayer(snapMarker)) snapMarker.addTo(ctx.map)
        }
    } else {
        snapLatLng = null
        snapActive = false
        if (snapMarker && ctx.map.hasLayer(snapMarker)) ctx.map.removeLayer(snapMarker)
    }
}

// ─── EDIT-MODE SNAPPING ───────────────────────────────────────────────────────

export let editDragActive  = false  // true only while a vertex is being dragged
export let editModeActive  = false  // true from hookEditHandles until disableSnapping

function hookMarker(marker: any, layer: L.Layer): void {
    // Remove any previously attached snap handlers before re-attaching.
    if (marker._snapDragStart) marker.off('dragstart', marker._snapDragStart)
    if (marker._snapDrag)      marker.off('drag',      marker._snapDrag)
    if (marker._snapDragEnd)   marker.off('dragend',   marker._snapDragEnd)

    marker._snapDragStart = () => {
        snapExclude    = layer
        editDragActive = true
        snapActive     = false
        snapLatLng     = null
        if (snapMarker && ctx.map.hasLayer(snapMarker))
            ctx.map.removeLayer(snapMarker)
    }

    marker._snapDrag = () => { /* position tracked via onSnapMove */ }

    marker._snapDragEnd = () => {
        // Capture snap state BEFORE clearing editDragActive.
        const snapped = snapActive && snapLatLng
            ? L.latLng(snapLatLng.lat, snapLatLng.lng)
            : null

        // Capture marker's current (un-snapped) position.
        // Geoman's own dragend handler fires first (it was registered before ours),
        // so layer._latlngs already reflects where the user released the mouse.
        const unsnappedPos = marker.getLatLng()

        editDragActive = false
        snapExclude    = null

        // ── Apply snap SYNCHRONOUSLY ──────────────────────────────────────────
        // Geoman 2.x fires pm:edit (with async save via setTimeout 0) from its
        // own dragend handler, which runs BEFORE ours.  Applying snap here —
        // synchronously, before returning — ensures that setTimeout 0 in the
        // pm:edit save handler reads the already-corrected coordinates.
        //
        // Using in-place mutation (ll.lat/lng = ...) instead of setLatLngs()
        // avoids creating new LatLng objects, which would invalidate Geoman's
        // internal _origLatLng references and produce a "shadow polygon".
        if (snapped) {
            if (layer instanceof L.Polygon) {
                const rings = layer.getLatLngs() as L.LatLng[][]
                const ring  = rings[0]
                if (ring) {
                    let closestIdx = -1, minDist = Infinity
                    ring.forEach((ll, i) => {
                        const d = unsnappedPos.distanceTo(ll)
                        if (d < minDist) { minDist = d; closestIdx = i }
                    })
                    if (closestIdx >= 0) {
                        ring[closestIdx].lat = snapped.lat
                        ring[closestIdx].lng = snapped.lng
                        layer.redraw()
                        marker.setLatLng(snapped)
                    }
                }
            } else if (layer instanceof L.Polyline) {
                const lls = layer.getLatLngs() as L.LatLng[]
                let closestIdx = -1, minDist = Infinity
                lls.forEach((ll, i) => {
                    const d = unsnappedPos.distanceTo(ll)
                    if (d < minDist) { minDist = d; closestIdx = i }
                })
                if (closestIdx >= 0) {
                    lls[closestIdx].lat = snapped.lat
                    lls[closestIdx].lng = snapped.lng
                    layer.redraw()
                    marker.setLatLng(snapped)
                }
            }
        }

        // Re-hook after state is stable so newly-converted midpoint markers
        // (ghost → real vertex) also get snap handlers.
        hookAllEditMarkers()
    }

    marker.on('dragstart', marker._snapDragStart)
    marker.on('drag',      marker._snapDrag)
    marker.on('dragend',   marker._snapDragEnd)
}

export function hookAllEditMarkers(): void {
    ctx.drawnItems.eachLayer((layer: L.Layer) => {
        const pm = (layer as any).pm
        if (!pm || !pm.enabled()) return

        // Geoman stores vertex markers in pm._markers.
        // Polygons:  pm._markers is L.Marker[][] (one sub-array per ring).
        // Polylines: pm._markers is L.Marker[].
        // Flatten one level to get a single array of marker objects.
        const raw = pm._markers as any
        if (!raw) return

        const isNested = Array.isArray(raw[0]) && !(raw[0] instanceof L.Marker)
        const flat: any[] = isNested ? (raw as any[][]).flat() : (raw as any[])

        flat.forEach((marker: any) => {
            if (marker && typeof marker.on === 'function') {
                hookMarker(marker, layer)
            }
        })
    })
}

export function hookEditHandles(): void {
    editModeActive = true
    setTimeout(hookAllEditMarkers, 100)
}

export function installSnapInterceptors(): void {
    ctx.map.on('mousemove', (e: any) => {
        if (snapActive && snapLatLng) e.latlng = snapLatLng
    })
    ctx.map.on('click', (e: any) => {
        if (snapActive && snapLatLng) e.latlng = snapLatLng
    })
    ctx.map.on('mousedown', (e: any) => {
        if (snapActive && snapLatLng) e.latlng = snapLatLng
    })
}


export function enableSnapping(mode: 'districts' | 'roads', excludeLayer?: L.Layer, phaseKey?: string): void {
    activeSnapMode = mode
    snapExclude    = excludeLayer ?? null
    snapPhaseKey   = phaseKey ?? null
    document.addEventListener('mousemove',  onSnapMove,      true)
    document.addEventListener('mousedown',  onSnapMouseDown, true)
    document.addEventListener('mouseup',    onSnapMouseUp,   true)
}

export function disableSnapping(): void {
    activeSnapMode = null
    snapExclude    = null
    snapPhaseKey   = null
    editModeActive = false
    editDragActive = false
    snapFrozen     = false
    document.removeEventListener('mousemove',  onSnapMove,      true)
    document.removeEventListener('mousedown',  onSnapMouseDown, true)
    document.removeEventListener('mouseup',    onSnapMouseUp,   true)
    if (snapMarker && ctx.map.hasLayer(snapMarker)) ctx.map.removeLayer(snapMarker)
    snapActive = false
    snapLatLng = null
}
