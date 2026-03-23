// ─── SNAP GEOMETRY HELPERS ────────────────────────────────────────────────────
// Pure, stateless geometry functions used by the snap engine (snapping.ts).
// No access to module state, featureLayers, or ctx — only math.
// Extracted from snapping.ts to keep that file focused on the state machine.

declare const L: typeof import('leaflet')

// ─── PRIMITIVES ───────────────────────────────────────────────────────────────

/** Closest point on segment [a, b] to cursor point mp (in layer-pixel space). */
export function closestOnSegment(
    mp: L.Point, a: L.LatLng, b: L.LatLng,
    map: L.Map,
): L.LatLng | null {
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

/**
 * Closest point on a Leaflet Circle's visual perimeter (pixel space).
 * Returns null if the circle has zero radius or no internal pixel data yet.
 */
export function closestOnCirclePerimeter(
    mp: L.Point,
    circle: L.Circle,
): { ll: L.LatLng; dist: number } | null {
    try {
        const c         = circle as any
        const centerPx: L.Point = c._point
        const radiusPx: number  = c._radius
        if (!centerPx || !radiusPx || radiusPx === 0) return null
        const dx = mp.x - centerPx.x, dy = mp.y - centerPx.y
        const cursorDist = Math.hypot(dx, dy)
        if (cursorDist === 0) return null
        const snapPx = L.point(
            centerPx.x + (dx / cursorDist) * radiusPx,
            centerPx.y + (dy / cursorDist) * radiusPx,
        )
        const snapLL = (circle as any)._map.layerPointToLatLng(snapPx)
        return { ll: snapLL, dist: Math.abs(cursorDist - radiusPx) }
    } catch { return null }
}

/** Pixel distance from cursor point mp to a map coordinate. */
export function pixelDist(mp: L.Point, ll: L.LatLng, map: L.Map): number {
    try {
        const p = map.latLngToLayerPoint(ll)
        return Math.hypot(p.x - mp.x, p.y - mp.y)
    } catch { return Infinity }
}

// ─── NEAREST SNAP: POLYGONS / BOUNDARY RINGS ─────────────────────────────────

const CORNER_PX = 40
const EDGE_PX   = 40

/**
 * Finds the nearest snap point on a set of polygon rings.
 * Prefers vertices (within CORNER_PX) over edge midpoints (within EDGE_PX).
 */
export function nearestSnapPoint(
    mp: L.Point, rings: L.LatLng[][], map: L.Map,
): { ll: L.LatLng; dist: number } | null {
    let bestVertex: { ll: L.LatLng; dist: number } | null = null
    let bestEdge:   { ll: L.LatLng; dist: number } | null = null

    for (const ring of rings) {
        for (let i = 0; i < ring.length; i++) {
            const a = ring[i], b = ring[(i + 1) % ring.length]
            if (!a || !b) continue
            const dv = pixelDist(mp, a, map)
            if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: a, dist: dv }
            const cp = closestOnSegment(mp, a, b, map)
            if (cp) {
                const de = pixelDist(mp, cp, map)
                if (!bestEdge || de < bestEdge.dist) bestEdge = { ll: cp, dist: de }
            }
        }
    }

    if (bestVertex && bestVertex.dist <= CORNER_PX) return bestVertex
    if (bestEdge   && bestEdge.dist   <= EDGE_PX)   return bestEdge
    return null
}

// ─── NEAREST SNAP: ROAD NETWORK ───────────────────────────────────────────────

const CIRCLE_PX = 20

/**
 * Finds the nearest snap point in the road network (chains, area rings, city
 * center circles). City center perimeters win over vertices when closer.
 */
export function nearestSnapPointRoads(
    mp:      L.Point,
    chains:  L.LatLng[][],
    rings:   L.LatLng[][],
    points:  L.LatLng[],
    circles: L.Circle[],
    map:     L.Map,
): { ll: L.LatLng; dist: number } | null {
    let bestVertex: { ll: L.LatLng; dist: number } | null = null
    let bestEdge:   { ll: L.LatLng; dist: number } | null = null

    for (const pt of points) {
        if (!pt) continue
        const dv = pixelDist(mp, pt, map)
        if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: pt, dist: dv }
    }

    for (const chain of chains) {
        for (let i = 0; i < chain.length; i++) {
            const a = chain[i]
            if (!a) continue
            const dv = pixelDist(mp, a, map)
            if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: a, dist: dv }
            if (i < chain.length - 1) {
                const b = chain[i + 1]
                if (!b) continue
                const cp = closestOnSegment(mp, a, b, map)
                if (cp) {
                    const de = pixelDist(mp, cp, map)
                    if (!bestEdge || de < bestEdge.dist) bestEdge = { ll: cp, dist: de }
                }
            }
        }
    }

    for (const ring of rings) {
        for (let i = 0; i < ring.length; i++) {
            const a = ring[i], b = ring[(i + 1) % ring.length]
            if (!a || !b) continue
            const dv = pixelDist(mp, a, map)
            if (!bestVertex || dv < bestVertex.dist) bestVertex = { ll: a, dist: dv }
            const cp = closestOnSegment(mp, a, b, map)
            if (cp) {
                const de = pixelDist(mp, cp, map)
                if (!bestEdge || de < bestEdge.dist) bestEdge = { ll: cp, dist: de }
            }
        }
    }

    let bestCircle: { ll: L.LatLng; dist: number } | null = null
    for (const circle of circles) {
        const result = closestOnCirclePerimeter(mp, circle)
        if (result && (!bestCircle || result.dist < bestCircle.dist)) bestCircle = result
    }

    if (bestCircle  && bestCircle.dist  <= CIRCLE_PX) return bestCircle
    if (bestVertex  && bestVertex.dist  <= CORNER_PX) return bestVertex
    if (bestEdge    && bestEdge.dist    <= EDGE_PX)   return bestEdge
    return null
}
