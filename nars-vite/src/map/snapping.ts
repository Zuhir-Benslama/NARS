// ─── SNAP STATE MACHINE ───────────────────────────────────────────────────────
// Manages the active snap mode, snap marker, and edit-mode vertex hooks.
// Pure geometry helpers live in snap-geometry.ts.

import { ctx }           from './state'
import { featureLayers } from '../store'
import type { LayerEntry } from '../types'
import {
    nearestSnapPoint,
    nearestSnapPointRoads,
    closestOnSegment,
} from './snap-geometry'

declare const L: typeof import('leaflet')

// ─── MODULE STATE ─────────────────────────────────────────────────────────────

let snapActive:     boolean                      = false
let snapLatLng:     L.LatLng | null              = null
let snapMarker:     L.CircleMarker | null        = null
let activeSnapMode: 'districts' | 'roads' | null = null
let snapExclude:    L.Layer | null               = null
let snapPhaseKey:   string | null                = null
let snapFrozen      = false  // true from mousedown until after the click commits

// ─── SNAP SOURCE COLLECTORS ───────────────────────────────────────────────────
// These need module state (snapExclude, featureLayers) so they stay here.

function getSnapRings(phaseKey?: string): L.LatLng[][] {
    const rings: L.LatLng[][] = []
    ;[...featureLayers.areas, ...(phaseKey === 'areas' ? [] : featureLayers.districts)]
        .forEach(({ layer }) => {
            if (layer === snapExclude) return
            if (!(layer instanceof L.Polygon)) return
            const ring = (layer.getLatLngs()[0] as L.LatLng[])
                .filter(ll => ll && ll.lat != null && ll.lng != null)
            if (ring.length >= 2) rings.push(ring)
        })
    if (ctx.boundariesLayer) {
        ctx.boundariesLayer.eachLayer((bl: L.Layer) => {
            try {
                const extractRings = (arr: any): void => {
                    if (!arr.length) return
                    if (arr[0] instanceof L.LatLng) {
                        const r = (arr as L.LatLng[]).filter(ll => ll?.lat != null && ll?.lng != null)
                        if (r.length >= 2) rings.push(r)
                    } else { arr.forEach(extractRings) }
                }
                extractRings((bl as L.Polygon).getLatLngs())
            } catch { /* skip */ }
        })
    }
    return rings
}

function getRoadSnapSources(): {
    chains: L.LatLng[][], rings: L.LatLng[][], points: L.LatLng[], circles: L.Circle[]
} {
    const chains:  L.LatLng[][] = []
    const rings:   L.LatLng[][] = []
    const points:  L.LatLng[]   = []
    const circles: L.Circle[]   = []

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

// ─── MOUSE HANDLERS ───────────────────────────────────────────────────────────

function onSnapMouseDown(): void {
    if (!editModeActive && snapActive && snapLatLng) snapFrozen = true
}
function onSnapMouseUp(): void { snapFrozen = false }

function onSnapMove(e: MouseEvent): void {
    if (snapFrozen) return
    if (!ctx.map.getContainer().contains(e.target as Node)) return

    if (editModeActive && !editDragActive) {
        snapActive = false; snapLatLng = null
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
        snap = nearestSnapPoint(mp, rings, ctx.map)
    } else if (activeSnapMode === 'roads') {
        const { chains, rings, points, circles } = getRoadSnapSources()
        if (!chains.length && !rings.length && !points.length && !circles.length) {
            snapActive = false; snapLatLng = null; return
        }
        snap = nearestSnapPointRoads(mp, chains, rings, points, circles, ctx.map)
        snapColor = '#3498db'
    } else { snapActive = false; snapLatLng = null; return }

    if (snap) {
        snapLatLng = snap.ll; snapActive = true
        if (!ctx.map.getPane('snapPane')) {
            ctx.map.createPane('snapPane')
            ctx.map.getPane('snapPane')!.style.zIndex = '9999'
        }
        if (!snapMarker) {
            snapMarker = L.circleMarker(snap.ll, {
                radius: 8, color: snapColor, weight: 2.5,
                fillColor: '#fff', fillOpacity: 1, interactive: false, pane: 'snapPane',
            } as any).addTo(ctx.map)
        } else {
            snapMarker.setLatLng(snap.ll)
            snapMarker.setStyle({ color: snapColor })
            if (!ctx.map.hasLayer(snapMarker)) snapMarker.addTo(ctx.map)
        }
    } else {
        snapLatLng = null; snapActive = false
        if (snapMarker && ctx.map.hasLayer(snapMarker)) ctx.map.removeLayer(snapMarker)
    }
}

// ─── EDIT-MODE VERTEX HOOKS ───────────────────────────────────────────────────

export let editDragActive = false
export let editModeActive = false

function hookMarker(marker: any, layer: L.Layer): void {
    if (marker._snapDragStart) marker.off('dragstart', marker._snapDragStart)
    if (marker._snapDrag)      marker.off('drag',      marker._snapDrag)
    if (marker._snapDragEnd)   marker.off('dragend',   marker._snapDragEnd)

    marker._snapDragStart = () => {
        snapExclude = layer; editDragActive = true; snapActive = false; snapLatLng = null
        if (snapMarker && ctx.map.hasLayer(snapMarker)) ctx.map.removeLayer(snapMarker)
    }
    marker._snapDrag = () => { /* position tracked via onSnapMove */ }
    marker._snapDragEnd = () => {
        const snapped     = snapActive && snapLatLng ? L.latLng(snapLatLng.lat, snapLatLng.lng) : null
        const unsnappedPos = marker.getLatLng()
        editDragActive = false; snapExclude = null

        // Apply snap synchronously so Geoman's deferred pm:edit save reads
        // corrected coordinates. Use in-place mutation to preserve Geoman's
        // internal _origLatLng references (avoids the "shadow polygon" bug).
        if (snapped) {
            if (layer instanceof L.Polygon) {
                const ring = (layer.getLatLngs() as L.LatLng[][])[0]
                if (ring) {
                    let closestIdx = -1, minDist = Infinity
                    ring.forEach((ll, i) => {
                        const d = unsnappedPos.distanceTo(ll)
                        if (d < minDist) { minDist = d; closestIdx = i }
                    })
                    if (closestIdx >= 0) {
                        ring[closestIdx].lat = snapped.lat; ring[closestIdx].lng = snapped.lng
                        layer.redraw(); marker.setLatLng(snapped)
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
                    lls[closestIdx].lat = snapped.lat; lls[closestIdx].lng = snapped.lng
                    layer.redraw(); marker.setLatLng(snapped)
                }
            }
        }

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
        const raw = pm._markers as any
        if (!raw) return
        const isNested = Array.isArray(raw[0]) && !(raw[0] instanceof L.Marker)
        const flat: any[] = isNested ? (raw as any[][]).flat() : (raw as any[])
        flat.forEach((marker: any) => {
            if (marker && typeof marker.on === 'function') hookMarker(marker, layer)
        })
    })
}

export function hookEditHandles(): void {
    editModeActive = true
    setTimeout(hookAllEditMarkers, 100)
}

// ─── PUBLIC API ───────────────────────────────────────────────────────────────

export function installSnapInterceptors(): void {
    ctx.map.on('mousemove', (e: any) => { if (snapActive && snapLatLng) e.latlng = snapLatLng })
    ctx.map.on('click',     (e: any) => { if (snapActive && snapLatLng) e.latlng = snapLatLng })
    ctx.map.on('mousedown', (e: any) => { if (snapActive && snapLatLng) e.latlng = snapLatLng })
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
    activeSnapMode = null; snapExclude = null; snapPhaseKey = null
    editModeActive = false; editDragActive = false; snapFrozen = false
    document.removeEventListener('mousemove',  onSnapMove,      true)
    document.removeEventListener('mousedown',  onSnapMouseDown, true)
    document.removeEventListener('mouseup',    onSnapMouseUp,   true)
    if (snapMarker && ctx.map.hasLayer(snapMarker)) ctx.map.removeLayer(snapMarker)
    snapActive = false; snapLatLng = null
}
