// ─── SNAP STATE MACHINE ───────────────────────────────────────────────────────
// Snap priority (highest first): circle → vertex → midpoint → edge
// Only searches phases that are ≤ current phase (completed phases), not future ones.
//
// Matching the reference (nars-vite/Leaflet) snapping.ts:
// - Snap freeze on mousedown (prevents cursor jump during click)
// - Vertex + edge snapping for polygons/roads
// - Circle perimeter snapping for city centers
// - Edit-mode vertex hooking (in-place ring mutation on dragend)
// - installSnapInterceptors patches map events for drawing-mode snap

import { ctx } from './state'
import { store } from '../store'
import { useLayerStore } from '../stores/layerStore'
import type { LayerState } from '../stores/layerStore'
import { SNAP_CONFIG } from '../config'
import { PHASES } from '../phases'
import { unpatchGeomanMarker, repatchMarker, isSnappingEnabled, setSnappingEnabled } from './draw-complete'
import { closestOnSegmentProjected, closestOnCirclePerimeter, pixelDist } from './snap-geometry'

// ─── DEV WINDOW EXTENSION ─────────────────────────────────────────────────────

declare global {
    interface Window {
        __narsSnapLatLng?: { lat: number; lng: number } | null
    }
}

// ─── THRESHOLDS ───────────────────────────────────────────────────────────────
// Sourced from SNAP_CONFIG to keep thresholds centralized.

const CORNER_PX = SNAP_CONFIG.thresholds.vertex
const EDGE_PX = SNAP_CONFIG.thresholds.edge
const CIRCLE_PX = SNAP_CONFIG.thresholds.circle
const MIDPOINT_PX = SNAP_CONFIG.thresholds.midpoint

// ─── SNAP STATE ───────────────────────────────────────────────────────────────

// Cursor state
let crosshairActive = false

// Snap DOM elements
let snapMarker: HTMLDivElement | null = null
let snapCursor: HTMLDivElement | null = null

// Snap position
let snapActive = false
let snapLatLng: { lat: number; lng: number } | null = null

// Snap freeze: prevents snap from jumping during mousedown→click sequence.
let snapFrozen = false
export function isSnapFrozen(): boolean {
    return snapFrozen
}

/** Returns the frozen snap position (if active), or null. */
export function getFrozenSnapPos(): { lat: number; lng: number } | null {
    return snapFrozen && snapLatLng ? { lat: snapLatLng.lat, lng: snapLatLng.lng } : null
}

// Edit mode state
export let editModeActive = false
export function setEditModeActive(v: boolean): void {
    editModeActive = v
}
export let editDragActive = false
export function setEditDragActive(v: boolean): void {
    editDragActive = v
}
let snapExclude: string | null = null
export function setSnapExclude(id: string | null): void {
    snapExclude = id
}

// rAF debounce for onSnapMove
let snapRafId: number | null = null
let snapPendingEvent: MouseEvent | null = null

export function getActiveSnapPhases(): string[] {
    // During edit mode, allow snapping to all phases that have data (not just
    // the UI-selected one). This ensures roads can snap to other roads even
    // when the user is in the areas phase.
    if (editModeActive) {
        const layerStore = useLayerStore()
        const state = layerStore.$state as LayerState
        return Object.keys(state).filter((key) => {
            const entries = state[key as keyof LayerState]
            return entries && entries.length > 0
        })
    }

    const currentPhaseKey = PHASES[store.currentPhase]?.key ?? ''
    const allowedTargets = SNAP_CONFIG.snapTargets[currentPhaseKey as keyof typeof SNAP_CONFIG.snapTargets] ?? []

    const completedPhaseKeys = new Set<string>()
    for (let i = 0; i <= store.currentPhase; i++) {
        completedPhaseKeys.add(PHASES[i].key)
    }

    return ([...allowedTargets] as string[]).filter((key) => completedPhaseKeys.has(key))
}

// ─── CROSSHAIR CURSOR ─────────────────────────────────────────────────────────

export function enableCrosshair(): void {
    if (crosshairActive) return
    crosshairActive = true
    ctx.map.getCanvas().style.cursor = 'crosshair'
}

export function disableCrosshair(): void {
    if (!crosshairActive) return
    crosshairActive = false
    ctx.map.getCanvas().style.cursor = ''
}

// ─── SNAPPING ─────────────────────────────────────────────────────────────────

type SnapType = 'vertex' | 'midpoint' | 'edge' | 'circle'

/** Result of {@link findNearestSnap} — exported for draw-mode marker patching. */
export type SnapResult = { lat: number; lng: number; type: SnapType }

/** Internal search types — used only within findNearestSnap. */
type SnapCandidate = { lat: number; lng: number; dist: number }
type ProjectedVertex = { lat: number; lng: number; px: number; py: number }
type ProjectedSegment = {
    ax: number
    ay: number
    bx: number
    by: number
    alat: number
    alng: number
    blat: number
    blng: number
}

/**
 * Geoman passes the first placed vertex to its snapping helper for closing rings.
 * Our setLngLat patch uses {@link findNearestSnap} only, so we merge in that
 * in-progress first vertex when it is closer than any external snap.
 */
export function mergeExternalSnapWithDrawFirstVertex(
    cursorX: number,
    cursorY: number,
    external: SnapResult | null,
    project: (ll: [number, number]) => { x: number; y: number },
): SnapResult | null {
    const gm = ctx.geoman as
        | {
              actionInstances?: Record<string, { lineDrawer?: { shapeLngLats?: [number, number][] } }>
          }
        | undefined
    const ld = gm?.actionInstances?.draw__polygon?.lineDrawer ?? gm?.actionInstances?.draw__line?.lineDrawer
    const sh = ld?.shapeLngLats
    if (!sh?.length) return external

    const first = sh[0]
    const dFirst = pixelDist(cursorX, cursorY, first[0], first[1], project)
    if (dFirst === null || dFirst >= CORNER_PX) return external

    const firstSnap: SnapResult = { lng: first[0], lat: first[1], type: 'vertex' }
    if (!external) return firstSnap
    const dExt = pixelDist(cursorX, cursorY, external.lng, external.lat, project)
    return dFirst <= (dExt ?? Infinity) ? firstSnap : external
}

// ─── SNAP LIFECYCLE ───────────────────────────────────────────────────────────
// Matching the reference's snapFrozen behavior.

function onMouseDown(): void {
    // Matching the reference: only freeze snap in draw mode, not edit mode.
    if (!editModeActive && snapActive && snapLatLng) snapFrozen = true
}
function onMouseUp(): void {
    snapFrozen = false
}

export function enableSnapping(): void {
    if (isSnappingEnabled()) return
    setSnappingEnabled(true)
    snapActive = true
    snapExclude = null
    ctx.map.getContainer().addEventListener('mousemove', onSnapMove, true)
    ctx.map.getContainer().addEventListener('mousedown', onMouseDown, true)
    ctx.map.getContainer().addEventListener('mouseup', onMouseUp, true)
    // Re-patch the Geoman marker so it applies snap coordinates again
    repatchMarker()
}

export function disableSnapping(): void {
    if (!isSnappingEnabled()) return
    setSnappingEnabled(false)
    snapActive = false
    snapExclude = null
    snapFrozen = false
    editModeActive = false
    if (snapRafId !== null) {
        cancelAnimationFrame(snapRafId)
        snapRafId = null
    }
    snapPendingEvent = null
    ctx.map.getContainer().removeEventListener('mousemove', onSnapMove, true)
    ctx.map.getContainer().removeEventListener('mousedown', onMouseDown, true)
    ctx.map.getContainer().removeEventListener('mouseup', onMouseUp, true)
    if (snapMarker) {
        snapMarker.remove()
        snapMarker = null
    }
    if (snapCursor) {
        snapCursor.remove()
        snapCursor = null
    }
    if (import.meta.env.DEV) window.__narsSnapLatLng = null
    // Reset cursor to crosshair if in drawing mode, otherwise to default.
    ctx.map.getCanvas().style.cursor = crosshairActive ? 'crosshair' : ''
    // Unpatch the Geoman marker so it stops applying snap coordinates
    unpatchGeomanMarker()
}

/** Toggle snapping on/off and return the new state. */
export function toggleSnapping(): boolean {
    if (isSnappingEnabled()) {
        disableSnapping()
        return false
    } else {
        enableSnapping()
        return true
    }
}

export function isSnappingActive(): boolean {
    return snapActive
}

// ─── SNAP EVENT ───────────────────────────────────────────────────────────────

function onSnapMove(e: MouseEvent): void {
    snapPendingEvent = e
    if (snapRafId !== null) return
    snapRafId = requestAnimationFrame(processSnapMove)
}

// ─── HMR CLEANUP ─────────────────────────────────────────────────────────────
// Cancel orphaned rAF loops when the module is hot-replaced during development.

if (import.meta.hot) {
    import.meta.hot.dispose(() => {
        if (snapRafId !== null) {
            cancelAnimationFrame(snapRafId)
            snapRafId = null
        }
    })
}

function processSnapMove(): void {
    snapRafId = null
    const e = snapPendingEvent
    snapPendingEvent = null // Clear immediately before any processing
    if (!e || snapFrozen) return

    // Guard: ignore mousemove events from outside the map container.
    if (!ctx.map.getContainer().contains(e.target as Node)) return

    // Edit-mode pause: when editing, snap is disabled unless a vertex is
    // actively being dragged (editDragActive). Matching the reference.
    if (editModeActive && !editDragActive) {
        if (snapActive) {
            snapActive = false
            snapLatLng = null
            if (snapMarker) {
                snapMarker.remove()
                snapMarker = null
            }
            if (snapCursor) {
                snapCursor.remove()
                snapCursor = null
            }
        }
        return
    }

    const activePhases = getActiveSnapPhases()
    if (activePhases.length === 0) return

    const rect = ctx.map.getContainer().getBoundingClientRect()
    const x = e.clientX - rect.left
    const y = e.clientY - rect.top

    const snap = findNearestSnap(x, y, activePhases, false)
    if (snap) {
        snapActive = true
        snapLatLng = { lat: snap.lat, lng: snap.lng }
        const pos = ctx.map.project([snap.lng, snap.lat])
        showSnapIndicator(pos.x, pos.y, snap.type)
        if (import.meta.env.DEV) window.__narsSnapLatLng = snapLatLng
    } else {
        snapActive = false
        snapLatLng = null
        if (import.meta.env.DEV) window.__narsSnapLatLng = null
        if (snapMarker) {
            snapMarker.remove()
            snapMarker = null
        }
        if (snapCursor) {
            snapCursor.remove()
            snapCursor = null
        }
        ctx.map.getCanvas().style.cursor = crosshairActive ? 'crosshair' : ''
    }
}

// ─── SNAP INDICATOR STYLES ────────────────────────────────────────────────────
// Pre-defined style templates to avoid inline CSS string walls.

const SNAP_COLORS: Record<SnapType, string> = {
    vertex: '#f39c12',
    midpoint: '#f39c12',
    edge: '#27ae60',
    circle: '#e74c3c',
}

const MARKER_STYLE: Record<SnapType, string> = {
    vertex: 'width:16px;height:16px;background:yellow;border:3px solid {color};border-radius:50%;box-shadow:0 0 8px rgba(0,0,0,0.5);',
    midpoint:
        'width:12px;height:12px;background:{color};border:2px solid white;border-radius:2px;transform:translate(-50%,-50%) rotate(45deg);box-shadow:0 0 6px rgba(0,0,0,0.5);',
    circle: 'width:10px;height:10px;background:{color};border:2px solid white;border-radius:50%;box-shadow:0 0 6px rgba(0,0,0,0.5);',
    edge: 'width:12px;height:12px;background:transparent;border:2px solid {color};border-radius:2px;box-shadow:0 0 6px rgba(0,0,0,0.4);',
}

function showSnapIndicator(px: number, py: number, type: SnapType): void {
    const color = SNAP_COLORS[type]
    if (!snapMarker) {
        snapMarker = document.createElement('div')
        ctx.map.getContainer().appendChild(snapMarker)
    }

    const position = `position:absolute;pointer-events:none;z-index:9998;transform:translate(-50%,-50%);left:${px}px;top:${py}px;`
    const shape = MARKER_STYLE[type].replace('{color}', color)
    snapMarker.style.cssText = position + shape

    ctx.map.getCanvas().style.cursor = 'crosshair'
    if (!snapCursor) {
        snapCursor = document.createElement('div')
        snapCursor.className = 'nars-snap-crosshair'
        ctx.map.getContainer().appendChild(snapCursor)
    }
    // Single element with CSS crosshair — matching reference's single L.circleMarker approach.
    // Uses ::before/::after pseudo-elements for the 4 arms (white + colored overlay).
    snapCursor.style.cssText = `
        position:absolute;pointer-events:none;z-index:10000;
        left:${px}px;top:${py}px;
        --snap-color:${color};
    `
}

// ─── SNAP SOURCE COLLECTORS ───────────────────────────────────────────────────
// Collect snap geometry from all relevant sources, matching reference behavior.
// Each accepts an optional excludeId; if omitted falls back to the module-level snapExclude.

/** Rings from polygon feature layers (areas, districts, publicBuildings, publicSpaces) */
export function getSnapRings(phaseKeys: string[], excludeId?: string | null): Array<{ lat: number; lng: number }[]> {
    const exclude = excludeId ?? snapExclude
    const rings: Array<{ lat: number; lng: number }[]> = []
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    for (const key of phaseKeys) {
        const entries = state[key as keyof LayerState]
        if (!entries) continue
        for (const entry of entries) {
            if (entry.id === exclude) continue
            if (entry.type !== 'polygon') continue
            const coords = entry.data.coordinates
            if (!coords || coords.length < 3) continue
            rings.push(coords)
        }
    }

    // Commune boundary rings (matching reference: boundariesLayer)
    if (ctx.boundariesGeoJson) {
        const extractRings = (coords: unknown): void => {
            if (!Array.isArray(coords) || coords.length === 0) return
            const head = (coords as unknown[])[0]
            // A linear ring is [[lng, lat], ...] — first element must be a coordinate pair, not a number.
            const isLinearRing =
                Array.isArray(head) && head.length >= 2 && typeof head[0] === 'number' && typeof head[1] === 'number'
            if (isLinearRing) {
                const ring = (coords as [number, number][]).map(([lng, lat]) => ({ lat, lng }))
                if (ring.length >= 3) rings.push(ring)
                return
            }
            // Deeper nesting (e.g. polygon array levels) — recurse into each part
            for (const part of coords as unknown[]) extractRings(part)
        }
        for (const feature of ctx.boundariesGeoJson.features) {
            const geom = feature.geometry
            if (geom.type === 'Polygon') {
                geom.coordinates.forEach((ring: unknown) => extractRings(ring))
            } else if (geom.type === 'MultiPolygon') {
                geom.coordinates.forEach((poly: unknown) =>
                    (poly as unknown[]).forEach((ring: unknown) => extractRings(ring)),
                )
            }
        }
    }

    return rings
}

/** Road chains from roads layer */
export function getRoadChains(phaseKeys: string[], excludeId?: string | null): Array<{ lat: number; lng: number }[]> {
    const exclude = excludeId ?? snapExclude
    const chains: Array<{ lat: number; lng: number }>[] = []
    if (!phaseKeys.includes('roads')) return chains
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    const entries = state.roads
    if (!entries) return chains
    for (const entry of entries) {
        if (entry.id === exclude) continue
        if (entry.type !== 'line') continue
        const coords = entry.data.coordinates
        if (!coords || coords.length < 2) continue
        chains.push(coords)
    }
    return chains
}

/** City center points with radius info */
export function getCityCenterCircles(
    phaseKeys: string[],
    excludeId?: string | null,
): Array<{ lat: number; lng: number; radius: number }> {
    const exclude = excludeId ?? snapExclude
    const circles: Array<{ lat: number; lng: number; radius: number }> = []
    if (!phaseKeys.includes('cityCenter')) return circles
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    const entries = state.cityCenter
    if (!entries) return circles
    for (const entry of entries) {
        if (entry.id === exclude) continue
        const d = entry.data
        if (d.lat != null && d.lng != null && d.radius != null && d.radius > 0) {
            circles.push({ lat: d.lat, lng: d.lng, radius: d.radius as number })
        }
    }
    return circles
}

/** Point features (city centers, etc.) */
export function getSnapPoints(phaseKeys: string[], excludeId?: string | null): Array<{ lat: number; lng: number }> {
    const exclude = excludeId ?? snapExclude
    const points: Array<{ lat: number; lng: number }> = []
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    for (const key of phaseKeys) {
        const entries = state[key as keyof LayerState]
        if (!entries) continue
        for (const entry of entries) {
            if (entry.id === exclude) continue
            const d = entry.data
            if (d.lat != null && d.lng != null) {
                points.push({ lat: d.lat, lng: d.lng })
            }
        }
    }
    return points
}

// ─── SNAP SEARCH ──────────────────────────────────────────────────────────────

export function findNearestSnap(
    cursorX: number,
    cursorY: number,
    phaseKeys: string[],
    includeMidpoint: boolean,
    excludeId?: string | null,
): SnapResult | null {
    const best: {
        vertex: SnapCandidate | null
        midpoint: SnapCandidate | null
        edge: SnapCandidate | null
        circle: SnapCandidate | null
    } = { vertex: null, midpoint: null, edge: null, circle: null }

    const project = (ll: [number, number]) => ctx.map.project(ll)
    const unproject = (pt: [number, number]) => ctx.map.unproject(pt)

    // ── Viewport culling: compute extended bounds once ────────────────
    // Skip features entirely outside the visible viewport + margin.
    // This avoids O(N) project() calls for off-screen features.
    const bounds = ctx.map.getBounds()
    const CULL_PAD = 0.03 // ~3 km margin at zoom 15
    const inCullBox = (lat: number, lng: number): boolean =>
        lat >= bounds.getSouth() - CULL_PAD &&
        lat <= bounds.getNorth() + CULL_PAD &&
        lng >= bounds.getWest() - CULL_PAD &&
        lng <= bounds.getEast() + CULL_PAD

    // ── Pre-project all snap vertices once to avoid repeated projections ────
    // Each call to project()/unproject() involves a matrix transform. By
    // projecting all candidates upfront we reduce O(V+E) calls to O(V+E)
    // simple arithmetic comparisons in the search loop.
    const projectedVertices: ProjectedVertex[] = []
    const projectedSegments: ProjectedSegment[] = []

    const addRing = (ring: { lat: number; lng: number }[]): void => {
        for (let i = 0; i < ring.length; i++) {
            const v = ring[i]
            const p = project([v.lng, v.lat])
            // Add vertex only if it's in the cull box
            if (inCullBox(v.lat, v.lng)) {
                projectedVertices.push({ lat: v.lat, lng: v.lng, px: p.x, py: p.y })
            }
            // Create segment to next vertex regardless of cull — segment may still
            // intersect viewport even if one endpoint is outside.
            const j = (i + 1) % ring.length
            const b = ring[j]
            const pb = project([b.lng, b.lat])
            projectedSegments.push({
                ax: p.x,
                ay: p.y,
                bx: pb.x,
                by: pb.y,
                alat: v.lat,
                alng: v.lng,
                blat: b.lat,
                blng: b.lng,
            })
        }
    }

    const addChain = (chain: { lat: number; lng: number }[]): void => {
        for (let i = 0; i < chain.length; i++) {
            const v = chain[i]
            const p = project([v.lng, v.lat])
            if (inCullBox(v.lat, v.lng)) {
                projectedVertices.push({ lat: v.lat, lng: v.lng, px: p.x, py: p.y })
            }
            if (i < chain.length - 1) {
                const b = chain[i + 1]
                const pb = project([b.lng, b.lat])
                projectedSegments.push({
                    ax: p.x,
                    ay: p.y,
                    bx: pb.x,
                    by: pb.y,
                    alat: v.lat,
                    alng: v.lng,
                    blat: b.lat,
                    blng: b.lng,
                })
            }
        }
    }

    // Collect and project polygon rings
    for (const ring of getSnapRings(phaseKeys, excludeId)) addRing(ring)
    // Collect and project road chains
    for (const chain of getRoadChains(phaseKeys, excludeId)) addChain(chain)

    // Collect point features (city centers, etc.) — filter before projecting
    const snapPoints = getSnapPoints(phaseKeys, excludeId)
    const projectedPoints: ProjectedVertex[] = []
    for (const pt of snapPoints) {
        if (!inCullBox(pt.lat, pt.lng)) continue
        const p = project([pt.lng, pt.lat])
        projectedPoints.push({ lat: pt.lat, lng: pt.lng, px: p.x, py: p.y })
    }

    // ── Circle perimeters (city centers) ──────────────────────────────
    const circles = getCityCenterCircles(phaseKeys, excludeId)
    for (const c of circles) {
        const cp = closestOnCirclePerimeter(cursorX, cursorY, c.lng, c.lat, c.radius, project, unproject)
        if (cp && cp.dist < CIRCLE_PX && (!best.circle || cp.dist < best.circle.dist)) {
            best.circle = { lat: cp.lat, lng: cp.lng, dist: cp.dist }
        }
    }

    // ── Vertices from pre-projected points ────────────────────────────
    for (const v of projectedVertices) {
        const dv = Math.hypot(v.px - cursorX, v.py - cursorY)
        if (dv < CORNER_PX && (!best.vertex || dv < best.vertex.dist)) {
            best.vertex = { lat: v.lat, lng: v.lng, dist: dv }
        }
    }
    for (const pt of projectedPoints) {
        const dv = Math.hypot(pt.px - cursorX, pt.py - cursorY)
        if (dv < CORNER_PX && (!best.vertex || dv < best.vertex.dist)) {
            best.vertex = { lat: pt.lat, lng: pt.lng, dist: dv }
        }
    }

    // ── Midpoints and edges from pre-projected segments ───────────────
    if (includeMidpoint) {
        for (const s of projectedSegments) {
            const midPx = (s.ax + s.bx) / 2,
                midPy = (s.ay + s.by) / 2
            // Compute midpoint in pixel space, then unproject to lat/lng.
            // Using lat/lng averages distorts the position on Mercator projection,
            // especially on long segments or at higher latitudes.
            const midLL = unproject([midPx, midPy])
            const dm = Math.hypot(midPx - cursorX, midPy - cursorY)
            if (dm < MIDPOINT_PX && (!best.midpoint || dm < best.midpoint.dist)) {
                best.midpoint = { lat: midLL.lat, lng: midLL.lng, dist: dm }
            }
        }
    }

    for (const s of projectedSegments) {
        const cp = closestOnSegmentProjected(cursorX, cursorY, s.ax, s.ay, s.bx, s.by, s.alat, s.alng, unproject)
        if (cp) {
            const de = Math.hypot(cp.x - cursorX, cp.y - cursorY)
            if (de < EDGE_PX && (!best.edge || de < best.edge.dist)) {
                best.edge = { lat: cp.lat, lng: cp.lng, dist: de }
            }
        }
    }

    // ── Priority: circle → vertex → midpoint → edge ──────────────────
    if (best.circle) return { lat: best.circle.lat, lng: best.circle.lng, type: 'circle' as const }
    if (best.vertex) return { lat: best.vertex.lat, lng: best.vertex.lng, type: 'vertex' as const }
    if (best.midpoint) return { lat: best.midpoint.lat, lng: best.midpoint.lng, type: 'midpoint' as const }
    if (best.edge) return { lat: best.edge.lat, lng: best.edge.lng, type: 'edge' as const }
    return null
}

// ─── EDIT MODE SNAP ───────────────────────────────────────────────────────────

export function snapPointForEdit(
    cursorX: number,
    cursorY: number,
    excludeId: string | null,
): { lat: number; lng: number } | null {
    const result = findNearestSnap(cursorX, cursorY, getActiveSnapPhases(), true, excludeId)
    return result ? { lat: result.lat, lng: result.lng } : null
}

export function installSnapInterceptors(): void {
    // Patch map click/mousedown events so any code reading e.lngLat gets the
    // snapped value. The primary snapping is handled by Geoman's snapping helper
    // (patched in draw-events.ts), which controls the cursor and drawn coordinates.
    // This is a safety net for any other code that reads e.lngLat from click events.
    //
    // Using Object.defineProperty instead of direct assignment to avoid side effects
    // if other listeners already hold a reference to the original lngLat object.
    const map = ctx.map

    /* eslint-disable-next-line @typescript-eslint/no-explicit-any */
    const snapLngLat = (e: any): void => {
        if (!snapActive || !snapLatLng) return
        const { lng, lat } = snapLatLng
        try {
            Object.defineProperty(e, 'lngLat', {
                value: { lng, lat, toArray: () => [lng, lat] },
                writable: true,
                configurable: true,
            })
        } catch {
            // Property is non-configurable in this MapLibre version — skip safely.
        }
    }

    map.on('click', snapLngLat)
    map.on('mousedown', snapLngLat)
}
