// ─── ROAD DIRECTION COMPUTATION ───────────────────────────────────────────────
// Called once when the operator finishes the Roads phase and advances to House
// Entrances.  Road drawing order is discarded; direction is derived from
// network topology relative to the city center(s).
//
// Rules:
//   1. If a city center exists and a road is reachable from it through the
//      connected road network → orient the road AWAY from the city center.
//   2. All other roads (isolated from every city center, or no city center
//      placed) → geographic fallback: North→South if more vertical, else
//      East→West.

import { ctx }           from './state'
import { featureLayers } from '../store'
import { apiFetch }      from '../api'
import { addPolylineEndpoints } from './labels'
import type { LayerEntry }  from '../types'

declare const L: typeof import('leaflet')

// Two road endpoints are "connected" when they are within this distance (metres).
// Matches the 20 m backend connectivity check with a small tolerance buffer.
const ENDPOINT_THRESHOLD_M = 30

type Coord = { lat: number; lng: number }

function mDist(a: Coord, b: Coord): number {
    return L.latLng(a.lat, a.lng).distanceTo(L.latLng(b.lat, b.lng))
}

interface RoadNode {
    entry:   LayerEntry
    dbId:    number
    coords:  Coord[]   // original stored order — NOT mutated until apply step
    fromPt:  Coord     // algorithm-determined start (reference to coords[0] or coords[last])
    toPt:    Coord     // algorithm-determined end
    visited: boolean
}

// ─── GEOGRAPHIC FALLBACK ─────────────────────────────────────────────────────

function applyGeographicDirection(node: RoadNode): void {
    const first = node.coords[0]
    const last  = node.coords[node.coords.length - 1]
    const dLat  = Math.abs(last.lat - first.lat)
    const dLng  = Math.abs(last.lng - first.lng)

    if (dLat >= dLng) {
        // More N-S: start at northernmost (higher lat) → direction N→S
        if (first.lat >= last.lat) {
            node.fromPt = first; node.toPt = last    // already correct
        } else {
            node.fromPt = last;  node.toPt = first   // need to reverse
        }
    } else {
        // More E-W: start at easternmost (higher lng) → direction E→W
        if (first.lng >= last.lng) {
            node.fromPt = first; node.toPt = last
        } else {
            node.fromPt = last;  node.toPt = first
        }
    }
}

// ─── BFS FROM A SINGLE CITY CENTER ───────────────────────────────────────────
// Orients every road in the connected component of `startPt` so that it flows
// AWAY from the city center.  Stops at roads already visited by a prior BFS
// (i.e. another city center already claimed them — first one wins).

function bfsFromCityCenter(startPt: Coord, nodes: RoadNode[]): void {
    // Find the closest unvisited road endpoint to this city center.
    let seedNode:     RoadNode | null = null
    let seedDist      = Infinity
    let seedFromFirst = true   // whether fromPt should be coords[0]

    for (const node of nodes) {
        if (node.visited) continue
        const dFirst = mDist(startPt, node.coords[0])
        const dLast  = mDist(startPt, node.coords[node.coords.length - 1])
        if (dFirst < seedDist) { seedDist = dFirst; seedNode = node; seedFromFirst = true  }
        if (dLast  < seedDist) { seedDist = dLast;  seedNode = node; seedFromFirst = false }
    }

    // City center must be reasonably close to a road endpoint to anchor the BFS.
    // 200 m threshold: generous enough for any real commune layout.
    if (!seedNode || seedDist > 200) return

    // Seed road: fromPt = endpoint closer to city center (user came FROM there)
    if (seedFromFirst) {
        seedNode.fromPt = seedNode.coords[0]
        seedNode.toPt   = seedNode.coords[seedNode.coords.length - 1]
    } else {
        seedNode.fromPt = seedNode.coords[seedNode.coords.length - 1]
        seedNode.toPt   = seedNode.coords[0]
    }
    seedNode.visited = true

    // Queue: "far" endpoints of already-oriented roads (further from city center).
    // We propagate outward through these junction points.
    const queue: Coord[] = [seedNode.toPt]

    while (queue.length) {
        const ep = queue.shift()!

        for (const node of nodes) {
            if (node.visited) continue
            const dFirst = mDist(ep, node.coords[0])
            const dLast  = mDist(ep, node.coords[node.coords.length - 1])

            if (dFirst <= ENDPOINT_THRESHOLD_M) {
                // ep (far end of visited network) connects to node's FIRST point.
                // → Road flows first → last  (first is close to city center side,
                //   last is the new far end).  Keep original order.
                node.fromPt  = node.coords[0]
                node.toPt    = node.coords[node.coords.length - 1]
                node.visited = true
                queue.push(node.toPt)
            } else if (dLast <= ENDPOINT_THRESHOLD_M) {
                // ep connects to node's LAST point.
                // → Road flows last → first  (last is close to city center side).
                //   Reverse from original order.
                node.fromPt  = node.coords[node.coords.length - 1]
                node.toPt    = node.coords[0]
                node.visited = true
                queue.push(node.toPt)
            }
        }
    }
}

// ─── MAIN ENTRY POINT ────────────────────────────────────────────────────────

export async function computeAndApplyRoadDirections(): Promise<void> {
    const roads = featureLayers.roads as LayerEntry[]
    if (!roads.length) return

    // Build working nodes — fromPt/toPt start as original drawing order.
    const nodes: RoadNode[] = roads.map(entry => {
        const coords = entry.data.coordinates!
        return {
            entry,
            dbId:    (entry.layer as any)._dbId as number,
            coords,
            fromPt:  coords[0],
            toPt:    coords[coords.length - 1],
            visited: false,
        }
    })

    // ── Step 1: BFS from each city center ────────────────────────────────────
    for (const ccEntry of featureLayers.cityCenter as LayerEntry[]) {
        if (ccEntry.data.lat == null || ccEntry.data.lng == null) continue
        bfsFromCityCenter({ lat: ccEntry.data.lat, lng: ccEntry.data.lng }, nodes)
    }

    // ── Step 2: Geographic fallback for unreached roads ───────────────────────
    for (const node of nodes) {
        if (!node.visited) applyGeographicDirection(node)
    }

    // ── Step 3: Apply reversals, persist, refresh arrows ─────────────────────
    // Clear all endpoint arrow markers first so we can rebuild from scratch.
    ctx.lineEndpointLayer.clearLayers()
    for (const node of nodes) {
        ;(node.entry.layer as any)._endpointMarkers = []
    }

    for (const node of nodes) {
        // needsReverse: true when the algorithm chose coords[last] as the start
        // (reference comparison is safe — fromPt is always assigned directly
        // from node.coords[0] or node.coords[last], never a copy).
        const needsReverse = node.fromPt !== node.coords[0]

        if (needsReverse) {
            const reversed = [...node.coords].reverse()
            node.entry.data.coordinates = reversed
            ;(node.entry.layer as L.Polyline).setLatLngs(
                reversed.map(c => L.latLng(c.lat, c.lng))
            )
            try {
                await apiFetch(`/api/update/${node.dbId}`, {
                    method:  'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body:    JSON.stringify({ data: node.entry.data }),
                })
            } catch (err) {
                console.error(`Road direction save error (id=${node.dbId}):`, err)
            }
        }

        // Always re-add endpoint arrows so the visual direction is current.
        addPolylineEndpoints(node.entry.layer)
    }
}
