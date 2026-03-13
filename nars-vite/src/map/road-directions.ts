// ─── ROAD DIRECTION COMPUTATION ───────────────────────────────────────────────
// Two-phase algorithm:
//   Phase 1 — CONNECTION SCAN: single O(n²) pass builds a full topology graph.
//             Handles endpoint-to-endpoint AND endpoint-to-body (T-junctions).
//   Phase 2 — ORIENTATION: BFS from city center perimeter outward.
//             Unreached roads → geographic fallback (N→S or E→W).

import * as turf         from '@turf/turf'
import Graph             from 'graphology'

import { ctx }           from './state'
import { featureLayers } from '../store'
import { apiFetch }      from '../api'
import { addPolylineEndpoints } from './labels'
import type { LayerEntry } from '../types'

declare const L: typeof import('leaflet')

const CONNECT_M = 30

// ─── TYPES ────────────────────────────────────────────────────────────────────

type Coord = { lat: number; lng: number }

/** One logical segment of a road (a road split by T-junctions → multiple segments) */
interface Seg {
    coords:   Coord[]
    entry:    LayerEntry
    dbId:     number
    reversed: boolean
}

// ─── HELPERS ──────────────────────────────────────────────────────────────────

const toPt = (c: Coord) => turf.point([c.lng, c.lat])
const toLn = (cs: Coord[]) => turf.lineString(cs.map(c => [c.lng, c.lat]))
const dm   = (a: Coord, b: Coord) => turf.distance(toPt(a), toPt(b), { units: 'meters' })

function nk(c: Coord) { return `${c.lat.toFixed(5)},${c.lng.toFixed(5)}` }
function fromNk(k: string): Coord {
    const [lat, lng] = k.split(',').map(Number)
    return { lat, lng }
}

// ─── PHASE 1: CONNECTION SCAN → GRAPH ────────────────────────────────────────
//
// For every road:
//   a) Collect its own endpoints.
//   b) Scan every other road's endpoints — if one lands on THIS road's body
//      within CONNECT_M → record a T-junction split point.
//   c) Sort split points along the road, slice into segments.
//   d) Add each segment as an edge in the graph.
//
// Result: a Graphology graph where every edge is a Seg, with shared nodes
// at every connection point:
//   • endpoint-to-endpoint
//   • endpoint-to-body  (T-junction)
//   • body-to-body      (X-junction / crossing)

function buildConnectionGraph(roads: LayerEntry[]): { graph: Graph; segs: Map<string, Seg> } {
    const graph = new Graph({ multi: true, type: 'undirected' })
    const segs  = new Map<string, Seg>()

    // Reuse a shared node when within CONNECT_M of an existing one
    const resolveNode = (c: Coord): string => {
        for (const k of graph.nodes())
            if (dm(c, fromNk(k)) <= CONNECT_M) return k
        const k = nk(c)
        graph.addNode(k)
        return k
    }

    for (const road of roads) {
        const coords = road.data.coordinates
        if (!coords?.length) continue
        const dbId = (road.layer as any)._dbId as number
        const line = toLn(coords)

        // ── Find all junction points on this road ───────────────────────────
        // Covers T-junctions (endpoint-to-body) and X-junctions (body-to-body).
        const junctions: Array<{ segIdx: number; pt: Coord }> = []

        for (const other of roads) {
            if (other === road) continue
            const otherCoords = other.data.coordinates
            if (!otherCoords?.length) continue
            const otherLine = toLn(otherCoords)

            // ── T-junction: endpoint of `other` lands on body of `road` ─────
            for (const ep of [otherCoords[0], otherCoords[otherCoords.length - 1]]) {
                if (dm(ep, coords[0]) <= CONNECT_M) continue
                if (dm(ep, coords[coords.length - 1]) <= CONNECT_M) continue

                const nearest = turf.nearestPointOnLine(line, toPt(ep), { units: 'meters' })
                if ((nearest.properties.dist ?? Infinity) > CONNECT_M) continue

                junctions.push({
                    segIdx: nearest.properties.index ?? 0,
                    pt: {
                        lat: nearest.geometry.coordinates[1],
                        lng: nearest.geometry.coordinates[0],
                    },
                })
            }

            // ── X-junction: body of `other` crosses body of `road` ──────────
            const intersects = turf.lineIntersect(line, otherLine)
            for (const feature of intersects.features) {
                const pt: Coord = {
                    lat: feature.geometry.coordinates[1],
                    lng: feature.geometry.coordinates[0],
                }
                // Skip if intersection is at road's own endpoints
                if (dm(pt, coords[0]) <= CONNECT_M) continue
                if (dm(pt, coords[coords.length - 1]) <= CONNECT_M) continue

                const nearest = turf.nearestPointOnLine(line, toPt(pt), { units: 'meters' })
                junctions.push({
                    segIdx: nearest.properties.index ?? 0,
                    pt,
                })
            }
        }

        // ── Slice road into segments at junction points ──────────────────────
        // Sort by segment index along the road, deduplicate nearby points
        junctions.sort((a, b) => a.segIdx - b.segIdx)

        const splitPts: Array<{ segIdx: number; pt: Coord }> = []
        for (const j of junctions) {
            const last = splitPts[splitPts.length - 1]
            if (!last || dm(j.pt, last.pt) > CONNECT_M)
                splitPts.push(j)
        }

        // Build the list of sub-segments
        const slices: Coord[][] = []
        let prev = 0

        for (const { segIdx, pt } of splitPts) {
            if (segIdx <= prev) continue
            slices.push([...coords.slice(prev, segIdx + 1), pt])
            prev = segIdx
        }
        slices.push([...coords.slice(prev)])   // tail segment

        // ── Add each sub-segment as a graph edge ─────────────────────────────
        for (let i = 0; i < slices.length; i++) {
            const sc = slices[i]
            if (sc.length < 2) continue
            const na = resolveNode(sc[0])
            const nb = resolveNode(sc[sc.length - 1])
            if (na === nb) continue

            const ek = `${dbId}_${i}`
            graph.addEdgeWithKey(ek, na, nb)
            segs.set(ek, { coords: sc, entry: road, dbId, reversed: false })
        }
    }

    return { graph, segs }
}

// ─── PHASE 2A: GEOGRAPHIC FALLBACK ───────────────────────────────────────────

function geographicDirection(seg: Seg): void {
    const first = seg.coords[0]
    const last  = seg.coords[seg.coords.length - 1]
    const dLat  = Math.abs(last.lat - first.lat)
    const dLng  = Math.abs(last.lng - first.lng)

    seg.reversed = dLat >= dLng
        ? first.lat < last.lat    // N→S: start at highest lat
        : first.lng < last.lng    // E→W: start at highest lng
}

// ─── PHASE 2B: RECURSIVE DFS FROM CITY CENTER ───────────────────────────────
// City center is the origin. Perimeter-touching road nodes are seeds.
// Recursive DFS from each seed propagates orientation outward naturally:
//   - fromNode is always known at each call (the city-center side)
//   - visited guard ensures each edge is assigned exactly once
//   - when a road connects to multiple roads, the first recursive path
//     to reach it wins — which is the shortest path since we sort neighbors
//     by distance to city center before recursing

function orientFrom(
    fromNode:     string,
    graph:        Graph,
    segs:         Map<string, Seg>,
    visitedEdges: Set<string>,   // edge keys already oriented
    visitedRoads: Set<number>,   // dbIds already fully assigned — cannot be re-assigned
    distToCC:     Map<string, number>,
): void {
    // Collect unvisited edges from this node, sorted by distance of their
    // far endpoint to the city center — closest roads oriented first
    const edges = graph.edges(fromNode)
        .filter(ek => {
            if (visitedEdges.has(ek)) return false
            const seg = segs.get(ek)
            if (!seg) return false
            if (visitedRoads.has(seg.dbId)) return false  // road already assigned
            return true
        })
        .sort((ekA, ekB) => {
            const toA = graph.opposite(fromNode, ekA)
            const toB = graph.opposite(fromNode, ekB)
            return (distToCC.get(toA) ?? Infinity) - (distToCC.get(toB) ?? Infinity)
        })

    for (const ek of edges) {
        if (visitedEdges.has(ek)) continue   // claimed by sibling recursion
        const seg = segs.get(ek)!
        if (visitedRoads.has(seg.dbId)) continue   // road already assigned via another segment

        const toNode    = graph.opposite(fromNode, ek)
        const fromCoord = fromNk(fromNode)

        // Orient: coords[0] = from-side (closer to city center)
        seg.reversed =
            dm(seg.coords[0], fromCoord) >
            dm(seg.coords[seg.coords.length - 1], fromCoord)

        // Mark this edge AND the whole road as assigned — no other path can override
        visitedEdges.add(ek)
        visitedRoads.add(seg.dbId)

        // Also mark all other segments of the same road as visited
        for (const [otherEk, otherSeg] of segs) {
            if (otherSeg.dbId === seg.dbId) visitedEdges.add(otherEk)
        }

        // Recurse outward — toNode is now the from-side for its neighbors
        orientFrom(toNode, graph, segs, visitedEdges, visitedRoads, distToCC)
    }
}

function orientFromCityCenter(
    center:  Coord,
    radius:  number,
    graph:   Graph,
    segs:    Map<string, Seg>,
    visited: Set<string>,
): void {
    const seeds = graph.nodes().filter(k => {
        const d = turf.distance(toPt(center), toPt(fromNk(k)), { units: 'meters' })
        return Math.abs(d - radius) <= CONNECT_M
    })
    if (!seeds.length) return

    const distToCC = new Map<string, number>()
    for (const k of graph.nodes())
        distToCC.set(k, turf.distance(toPt(center), toPt(fromNk(k)), { units: 'meters' }))

    // visitedRoads tracks whole roads (by dbId) — once assigned, never re-assigned
    const visitedRoads = new Set<number>()

    for (const seed of seeds)
        orientFrom(seed, graph, segs, visited, visitedRoads, distToCC)
}

// ─── MAIN ENTRY POINT ─────────────────────────────────────────────────────────

export async function computeAndApplyRoadDirections(): Promise<void> {
    const roads = featureLayers.roads as LayerEntry[]
    if (!roads.length) return

    // ── Phase 1: build full connection graph ─────────────────────────────────
    const { graph, segs } = buildConnectionGraph(roads)
    const visited = new Set<string>()

    // ── Phase 2: orient ──────────────────────────────────────────────────────
    const cityCenters = (featureLayers.cityCenter as LayerEntry[])
        .filter(e => e.data.lat != null && e.data.lng != null)

    if (cityCenters.length > 0) {
        // City center present — all roads are connected to it directly or indirectly.
        // Recursive DFS from each city center orients every road away from it.
        // Geographic fallback is NOT used.
        for (const ccEntry of cityCenters) {
            const { lat, lng, radius } = ccEntry.data
            orientFromCityCenter({ lat: lat!, lng: lng! }, radius ?? 50, graph, segs, visited)
        }
    } else {
        // No city center — apply geographic fallback to every road.
        for (const seg of segs.values())
            geographicDirection(seg)
    }

    // ── Phase 3: reconcile segments → roads (majority vote per road) ─────────
    const votes = new Map<number, { fwd: number; rev: number; entry: LayerEntry; needsReverse?: boolean }>()
    for (const seg of segs.values()) {
        const v = votes.get(seg.dbId) ?? { fwd: 0, rev: 0, entry: seg.entry }
        seg.reversed ? v.rev++ : v.fwd++
        votes.set(seg.dbId, v)
    }

    // ── Dead-end correction (on whole roads, after vote) ────────────────────
    // Check each road's original endpoints against the graph.
    // If one endpoint touches only this road (degree 1 node) and the other
    // is connected to more roads (degree > 1), the road must flow FROM the
    // connected side regardless of what the vote decided.
    for (const [dbId, vote] of votes) {
        const coords = vote.entry.data.coordinates!
        if (!coords?.length) continue

        const first = coords[0]
        const last  = coords[coords.length - 1]

        // Find graph nodes corresponding to the road's original endpoints
        const nodeFirst = [...graph.nodes()].find(k => dm(fromNk(k), first) <= CONNECT_M)
        const nodeLast  = [...graph.nodes()].find(k => dm(fromNk(k), last)  <= CONNECT_M)



        if (!nodeFirst || !nodeLast) continue

        const degFirst = graph.degree(nodeFirst)
        const degLast  = graph.degree(nodeLast)

        if (degFirst !== 1 && degLast !== 1) continue   // both connected — vote is correct

        // When degFirst=1 AND degLast=1 the road was split by a T-junction on its body.
        // Both original endpoints are sub-segment tips — use distance to city center
        // to decide which end is the from-side (closer = from).
        // When only one side is degree 1, that side is the dead-end tip.
        const fromIsFirst = (() => {
            if (degFirst === 1 && degLast === 1) {
                // Both tips — compare straight-line distance to city center
                const ccEntries = (featureLayers.cityCenter as LayerEntry[])
                    .filter(e => e.data.lat != null && e.data.lng != null)
                if (!ccEntries.length) return degFirst >= degLast
                const cc = { lat: ccEntries[0].data.lat!, lng: ccEntries[0].data.lng! }
                return dm(first, cc) <= dm(last, cc)  // first is from-side if closer
            }
            return degFirst > degLast   // connected side (higher degree) is from-side
        })()
        vote.needsReverse = !fromIsFirst


    }

    // ── Phase 4: apply reversals, persist, refresh arrows ────────────────────
    ctx.lineEndpointLayer.clearLayers()
    for (const r of roads) { (r.layer as any)._endpointMarkers = [] }

    for (const [dbId, { fwd, rev, entry, needsReverse }] of votes) {
        const shouldReverse = needsReverse !== undefined ? needsReverse : rev > fwd
        if (shouldReverse) {
            const reversed = [...entry.data.coordinates!].reverse()
            entry.data.coordinates = reversed
            ;(entry.layer as L.Polyline).setLatLngs(reversed.map(c => L.latLng(c.lat, c.lng)))
            try {
                await apiFetch(`/api/update/${dbId}`, {
                    method: 'PUT', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ data: entry.data }),
                })
            } catch (err) { console.error(`Road direction save error (id=${dbId}):`, err) }
        }
        addPolylineEndpoints(entry.layer)
    }
}
