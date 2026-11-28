// ─── ROAD ORIENTATION ─────────────────────────────────────────────────────────
// Phase 2: closest-first BFS from the roundabouts perimeter outward to orient
// road segments (road category may take precedence over distance).
// Phase 2A: Geographic fallback when no roundabouts feature exists.

import Graph from "graphology"
import * as turfDistance from "@turf/distance"

import type { RoadFeatureData } from "../../types"
import { ROAD_TYPES } from "../../types/feature-types"

import type { Coord, Seg } from "./road-graph"
import { dm, toPt, fromNk, CONNECT_M } from "./road-graph"

const CATEGORY_RANK: Readonly<Record<string, number>> = {
  primary: 0,
  secondary: 1,
  tertiary: 2,
}
const UNKNOWN_CATEGORY_RANK = 3

/** Primary roads first, then secondary, then tertiary (unknown types last). */
function roadCategoryRank(seg: Seg | undefined): number {
  const key = (seg?.entry.data as RoadFeatureData | undefined)?.roadTypeKey
  const category = ROAD_TYPES.find((type) => type.key === key)?.category
  return category ? CATEGORY_RANK[category] : UNKNOWN_CATEGORY_RANK
}

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

// ─── PHASE 2B: CLOSEST-FIRST BFS FROM ROUNDABOUTS ─────────────────────────────

interface FrontierEdge {
  ek: string
  fromNode: string
  rank: number
  distance: number
}

/** Length of a road segment in metres, summed over its vertices. */
function segmentLength(seg: Seg): number {
  let length = 0
  for (let i = 1; i < seg.coords.length; i++) length += dm(seg.coords[i - 1], seg.coords[i])
  return length
}

export function orientFromRoundabouts(
  center: Coord,
  radius: number,
  graph: Graph,
  segs: Map<string, Seg>,
  visited: Set<string>,
  prioritizeCategory = false,
): void {
  const seeds = graph.nodes().filter((k: string) => {
    // `radius` (roundabouts ring) and CONNECT_M are in meters; turf distance
    // defaults to km, so request meters explicitly or the 30 m ring tolerance
    // silently becomes 30 km and every node becomes a seed.
    const d = turfDistance.distance(toPt(center), toPt(fromNk(k)), { units: "meters" })
    return Math.abs(d - radius) <= CONNECT_M
  })
  if (!seeds.length) return

  // Distance from the centre to each node along the roads traversed so far.
  // Seeds start at their straight-line distance to the roundabout ring; every
  // step after that adds the actual length of the road segment.
  const nodeDistance = new Map<string, number>()
  for (const seed of seeds)
    nodeDistance.set(
      seed,
      turfDistance.distance(toPt(center), toPt(fromNk(seed)), { units: "meters" }),
    )

  const visitedRoads = new Set<string>()
  const frontier: FrontierEdge[] = []

  const enqueueIncidentEdges = (node: string): void => {
    for (const ek of graph.edges(node)) {
      if (visited.has(ek)) continue
      const seg = segs.get(ek)
      if (!seg || visitedRoads.has(seg.dbId)) continue
      frontier.push({
        ek,
        fromNode: node,
        rank: prioritizeCategory ? roadCategoryRank(seg) : 0,
        distance: (nodeDistance.get(node) ?? Infinity) + segmentLength(seg),
      })
    }
  }

  for (const seed of seeds) enqueueIncidentEdges(seed)

  while (frontier.length) {
    // Serve the closest roads first; category rank takes precedence when enabled.
    frontier.sort((a, b) => a.rank - b.rank || a.distance - b.distance)
    const { ek, fromNode, distance } = frontier.shift()!
    if (visited.has(ek)) continue
    const seg = segs.get(ek)
    if (!seg || visitedRoads.has(seg.dbId)) continue

    const toNode = graph.opposite(fromNode, ek)
    if (distance < (nodeDistance.get(toNode) ?? Infinity)) nodeDistance.set(toNode, distance)
    const fromCoord = fromNk(fromNode)
    seg.reversed = dm(seg.coords[0], fromCoord) > dm(seg.coords[seg.coords.length - 1], fromCoord)

    visited.add(ek)
    visitedRoads.add(seg.dbId)

    for (const [otherEk, otherSeg] of segs) {
      if (otherSeg.dbId === seg.dbId) visited.add(otherEk)
    }

    enqueueIncidentEdges(toNode)
  }
}
