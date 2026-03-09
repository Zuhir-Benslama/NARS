// ─── VERTEX SNAPPING (districts + roads phases) ───────────────────────────────

import { ctx }          from './state'
import { featureLayers } from '../store'
import type { LayerEntry } from '../types'

declare const L: typeof import('leaflet') & {
    Draw: any
    Control: typeof import('leaflet').Control & { Draw: new (opts: any) => any }
    DrawEvents: any
}

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
                // getLatLngs() returns L.LatLng[] | L.LatLng[][] | L.LatLng[][][]
                // depending on the GeoJSON geometry type — flatten all levels
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

function getRoadSnapSources(): { chains: L.LatLng[][]; rings: L.LatLng[][]; points: L.LatLng[] } {
    const chains: L.LatLng[][] = []
    const rings:  L.LatLng[][] = []
    const points: L.LatLng[]   = []

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
        if (layer instanceof L.Marker) points.push(layer.getLatLng())
    })

    return { chains, rings, points }
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
    mp:     L.Point,
    chains: L.LatLng[][],
    rings:  L.LatLng[][],
    points: L.LatLng[],
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

    const CORNER_PX = 40, EDGE_PX = 40
    if (bestVertex && bestVertex.dist <= CORNER_PX) return bestVertex
    if (bestEdge   && bestEdge.dist   <= EDGE_PX)   return bestEdge
    return null
}

// ─── DRAW HANDLER ACCESS ─────────────────────────────────────────────────────

function getActiveDrawHandler(): any | null {
    const toolbar = (ctx.drawControl as any)?._toolbars?.draw
    if (!toolbar) return null
    const modes: Record<string, any> = toolbar._modes ?? {}
    for (const [, mode] of Object.entries(modes)) {
        const handler = mode?.handler
        if (handler?._enabled || handler?.enabled?.()) return handler
    }
    return null
}

let snapFrozen = false  // true from mousedown until after click

function onSnapMouseDown(): void {
    // Only freeze in draw mode — in edit mode the drag tracks the moving cursor
    if (!editModeActive && snapActive && snapLatLng) {
        snapFrozen = true
    }
}

function onSnapMouseUp(): void {
    snapFrozen = false
}

function onSnapMove(e: MouseEvent): void {
    if (snapFrozen) return  // mousedown in progress — hold snap state through click
    if (!ctx.map.getContainer().contains(e.target as Node)) return

    // In edit mode, only snap while a vertex is actually being dragged —
    // suppresses the snap circle appearing on the feature's own handles during hover
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
        const { chains, rings, points } = getRoadSnapSources()
        if (!chains.length && !rings.length && !points.length) { snapActive = false; snapLatLng = null; return }
        snap = nearestSnapPointRoads(mp, chains, rings, points)
        snapColor = '#3498db'
    } else {
        snapActive = false; snapLatLng = null; return
    }

    if (snap) {
        snapLatLng = snap.ll
        snapActive = true

        // Directly update the active draw handler's internal state.
        // Leaflet-draw places vertices using _mouseMarker.latlng (set via setLatLng)
        // and _currentLatLng — both read at mouseup time. No amount of event
        // interception works because addVertex reads from the marker object directly.
        const drawHandler = getActiveDrawHandler()
        if (drawHandler) {
            drawHandler._mouseMarker?.setLatLng(snap.ll)
            drawHandler._currentLatLng = snap.ll
        }
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
// Called directly inside the pm:editstart handler in index.ts.

export let editDragActive = false  // true only while a vertex is being dragged
export let editModeActive = false  // true from hookEditHandles until disableSnapping

function hookMarker(marker: any, layer: L.Layer): void {
    // Remove any previously attached snap handlers before re-attaching.
    // This is essential because ghost midpoint markers are converted in-place
    // to real vertex markers by Geoman — same object, so _snapHooked
    // would block re-hooking. Named handler refs let us cleanly replace them.
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

    marker._snapDrag = () => { /* marker position tracked via onSnapMove */ }

    marker._snapDragEnd = () => {
        // Capture snap state BEFORE clearing editDragActive.
        const snapped = snapActive && snapLatLng
            ? L.latLng(snapLatLng.lat, snapLatLng.lng)
            : null


        editDragActive = false
        snapExclude    = null
        hookAllEditMarkers()

        if (!snapped) return
        setTimeout(() => {
            // Use the layer closure variable directly
            const poly = layer
            if (poly instanceof L.Polygon) {
                const rings = poly.getLatLngs() as L.LatLng[][]
                if (rings[0] && marker._index !== undefined) {
                    rings[0][marker._index] = snapped
                    poly.setLatLngs(rings)
                }
            } else if (poly instanceof L.Polyline) {
                const lls = poly.getLatLngs() as L.LatLng[]
                if (marker._index !== undefined) {
                    lls[marker._index] = snapped
                    poly.setLatLngs(lls)
                }
            }
            marker.setLatLng(snapped)
        }, 0)
    }

    marker.on('dragstart', marker._snapDragStart)
    marker.on('drag',      marker._snapDrag)
    marker.on('dragend',   marker._snapDragEnd)
}

export function hookAllEditMarkers(): void {
    let layerCount = 0, markerCount = 0
    ctx.drawnItems.eachLayer((layer: L.Layer) => {
        // Check if Geoman editing is enabled on this layer
        const pm = (layer as any).pm
        if (!pm || !pm.enabled()) return
        layerCount++

        // Try to get the editor and its markers
        // Geoman stores markers in the editor's _markers array
        try {
            const editor = pm.getEditor?.()
            if (editor && editor._markers) {
                editor._markers.forEach((marker: any) => {
                    if (marker instanceof L.Marker) {
                        hookMarker(marker, layer)
                        markerCount++
                    }
                })
            }
        } catch (e) {
            // Ignore errors accessing Geoman internals
        }
    })
}

export function hookEditHandles(): void {
    editModeActive = true
    setTimeout(hookAllEditMarkers, 100)
}

export function installSnapInterceptors(): void {
    // Patch 1: leaflet-draw's _onMouseMove calls map.mouseEventToLayerPoint(e.originalEvent)
    // directly — it never reads e.latlng. Patch the method so it returns the snap
    // layer point when snapped.
    const orig = ctx.map.mouseEventToLayerPoint.bind(ctx.map)
    ctx.map.mouseEventToLayerPoint = function(e: MouseEvent): L.Point {
        if (snapActive && snapLatLng) return ctx.map.latLngToLayerPoint(snapLatLng)
        return orig(e)
    }

    // Patch 2: also rewrite e.latlng on Leaflet events as a belt-and-suspenders
    // measure, since some leaflet-draw versions read e.latlng instead.
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
