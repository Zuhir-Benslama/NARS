// ─── ROAD ORIENTATION ─────────────────────────────────────────────────────────
// Phase 2: BFS/DFS from city center perimeter outward to orient road segments.
// Phase 2A: Geographic fallback when no city center exists.

import Graph from "graphology"
import * as turfDistance from "@turf/distance"

import type { Coord, Seg } from "./road-graph"
import { dm, toPt, fromNk, CONNECT_M } from "./road-graph"

// ─── PHASE 2A: GEOGRAPHIC FALLBACK ───────────────────────────────────────────

export function geographicDirection(seg: Seg): void {
  const first = seg.coords[0]
  const last = seg.coords[seg.coords.length - 1]
  const dLat = Math.abs(last.lat - first.lat)
  const dLng = Math.abs(last.lng - first.lng)

  seg.reversed =
    dLat >= dLng
      ? first.lat < last.lat // N→S: start at highest lat
      : first.lng < last.lng // E→W: start at highest lng
}

// ─── PHASE 2B: RECURSIVE DFS FROM CITY CENTER ───────────────────────────────

function orientFrom(
  fromNode: string,
  graph: Graph,
  segs: Map<string, Seg>,
  visitedEdges: Set<string>,
  visitedRoads: Set<string>,
  distToCC: Map<string, number>,
): void {
  const edges = graph
    .edges(fromNode)
    .filter((ek: string) => {
      if (visitedEdges.has(ek)) return false
      const seg = segs.get(ek)
      if (!seg) return false
      if (visitedRoads.has(seg.dbId)) return false
      return true
    })
    .sort((ekA: string, ekB: string) => {
      const toA = graph.opposite(fromNode, ekA)
      const toB = graph.opposite(fromNode, ekB)
      return (distToCC.get(toA) ?? Infinity) - (distToCC.get(toB) ?? Infinity)
    })

  for (const ek of edges) {
    if (visitedEdges.has(ek)) continue
    const seg = segs.get(ek)!
    if (visitedRoads.has(seg.dbId)) continue

    const toNode = graph.opposite(fromNode, ek)
    const fromCoord = fromNk(fromNode)

    seg.reversed = dm(seg.coords[0], fromCoord) > dm(seg.coords[seg.coords.length - 1], fromCoord)

    visitedEdges.add(ek)
    visitedRoads.add(seg.dbId)

    for (const [otherEk, otherSeg] of segs) {
      if (otherSeg.dbId === seg.dbId) visitedEdges.add(otherEk)
    }

    orientFrom(toNode, graph, segs, visitedEdges, visitedRoads, distToCC)
  }
}

export function orientFromCityCenter(
  center: Coord,
  radius: number,
  graph: Graph,
  segs: Map<string, Seg>,
  visited: Set<string>,
): void {
  const seeds = graph.nodes().filter((k: string) => {
    // `radius` (city-center ring) and CONNECT_M are in meters; turf distance
    // defaults to km, so request meters explicitly or the 30 m ring tolerance
    // silently becomes 30 km and every node becomes a seed.
    const d = turfDistance.distance(toPt(center), toPt(fromNk(k)), { units: "meters" })
    return Math.abs(d - radius) <= CONNECT_M
  })
  if (!seeds.length) return

  const distToCC = new Map<string, number>()
  for (const k of graph.nodes())
    distToCC.set(k, turfDistance.distance(toPt(center), toPt(fromNk(k)), { units: "meters" }))

  const visitedRoads = new Set<string>()

  for (const seed of seeds) orientFrom(seed, graph, segs, visited, visitedRoads, distToCC)
}
