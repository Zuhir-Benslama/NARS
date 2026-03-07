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

// ─── SNAP SOURCE COLLECTORS ───────────────────────────────────────────────────

function getSnapRings(): L.LatLng[][] {
    const rings: L.LatLng[][] = []
    ;[...featureLayers.districts, ...featureLayers.areas].forEach(({ layer }) => {
        if (!(layer instanceof L.Polygon)) return
        const ring = (layer.getLatLngs()[0] as L.LatLng[]).filter(ll => ll && ll.lat != null && ll.lng != null)
        if (ring.length >= 2) rings.push(ring)
    })
    if (ctx.boundariesLayer) {
        ctx.boundariesLayer.eachLayer((bl: L.Layer) => {
            try {
                const lls  = (bl as L.Polygon).getLatLngs()
                const flat = (Array.isArray(lls[0]) ? lls[0] : lls) as L.LatLng[]
                const ring = flat.filter(ll => ll && ll.lat != null && ll.lng != null)
                if (ring.length >= 2) rings.push(ring)
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

// ─── MOUSE MOVE HANDLER ───────────────────────────────────────────────────────

function onSnapMove(e: MouseEvent): void {
    if (!ctx.map.getContainer().contains(e.target as Node)) return
    const rect = ctx.map.getContainer().getBoundingClientRect()
    const cp   = L.point(e.clientX - rect.left, e.clientY - rect.top)
    const mp   = ctx.map.containerPointToLayerPoint(cp)

    let snap: { ll: L.LatLng; dist: number } | null = null
    let snapColor = '#f39c12'

    if (activeSnapMode === 'districts') {
        const rings = getSnapRings()
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

// ─── ENABLE / DISABLE ─────────────────────────────────────────────────────────

export function enableSnapping(mode: 'districts' | 'roads'): void {
    activeSnapMode = mode
    document.addEventListener('mousemove', onSnapMove, true)

    if (!(ctx.map as any)._origMouseEventToLayerPoint) {
        const orig = ctx.map.mouseEventToLayerPoint.bind(ctx.map)
        ;(ctx.map as any)._origMouseEventToLayerPoint = orig
        ctx.map.mouseEventToLayerPoint = function(e: MouseEvent): L.Point {
            if (snapActive && snapLatLng) return ctx.map.latLngToLayerPoint(snapLatLng)
            return orig(e)
        }
    }
}

export function disableSnapping(): void {
    activeSnapMode = null
    document.removeEventListener('mousemove', onSnapMove, true)
    if ((ctx.map as any)._origMouseEventToLayerPoint) {
        ctx.map.mouseEventToLayerPoint = (ctx.map as any)._origMouseEventToLayerPoint
        delete (ctx.map as any)._origMouseEventToLayerPoint
    }
    if (snapMarker && ctx.map.hasLayer(snapMarker)) ctx.map.removeLayer(snapMarker)
    snapActive = false
    snapLatLng = null
}
