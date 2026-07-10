// ─── ROAD GRAPH ───────────────────────────────────────────────────────────────
// Phase 1: Connection scan builds a full topology graph from road coordinates.
// Handles endpoint-to-endpoint, endpoint-to-body (T-junctions),
// and body-to-body (X-junctions / crossings).
//
// Uses a spatial grid to reduce junction detection from O(n²) to O(n·k),
// where k is the number of roads in nearby cells.

import Graph from "graphology"
import * as turfHelpers from "@turf/helpers"
import * as turfNearest from "@turf/nearest-point-on-line"
import * as turfLineIntersect from "@turf/line-intersect"
import * as turfDistance from "@turf/distance"

import type { LayerEntry } from "../../types"

const CONNECT_M = 30 // Junction detection threshold in meters

// ─── HELPERS ──────────────────────────────────────────────────────────────────

type Coord = { lat: number; lng: number }

export const toPt = (c: Coord) => turfHelpers.point([c.lng, c.lat])
export const toLn = (cs: Coord[]) => turfHelpers.lineString(cs.map((c) => [c.lng, c.lat]))
export const dm = (a: Coord, b: Coord) =>
  turfDistance.distance(toPt(a), toPt(b), { units: "meters" })

export function nk(c: Coord) {
  return `${c.lat.toFixed(5)},${c.lng.toFixed(5)}`
}
export function fromNk(k: string): Coord {
  const [lat, lng] = k.split(",").map(Number)
  return { lat, lng }
}

// ─── SEGMENT TYPE ─────────────────────────────────────────────────────────────

/** One logical segment of a road (a road split by T-junctions → multiple segments) */
export interface Seg {
  coords: Coord[]
  entry: LayerEntry
  dbId: string
  reversed: boolean
}

// ─── SPATIAL GRID ─────────────────────────────────────────────────────────────

/**
 * Grid-based spatial index. Cell size is 2× CONNECT_M so any road
 * within CONNECT_M of a cell border is also present in the neighbor cell.
 * This guarantees we never miss a junction that straddles cells.
 */
const CELL_SIZE = CONNECT_M * 2

/** Expand a bounding box into the set of grid cells it overlaps. */
function cellsForBbox(
  minLat: number,
  maxLat: number,
  minLng: number,
  maxLng: number,
): Set<string> {
  const cells = new Set<string>()
  const r0 = Math.floor(minLat / CELL_SIZE)
  const r1 = Math.floor(maxLat / CELL_SIZE)
  const c0 = Math.floor(minLng / CELL_SIZE)
  const c1 = Math.floor(maxLng / CELL_SIZE)
  for (let r = r0; r <= r1; r++) {
    for (let c = c0; c <= c1; c++) {
      cells.add(`${r},${c}`)
    }
  }
  return cells
}

// ─── PHASE 1: CONNECTION SCAN → GRAPH ────────────────────────────────────────

export function buildConnectionGraph(roads: LayerEntry[]): {
  graph: Graph
  segs: Map<string, Seg>
} {
  const graph = new Graph({ multi: true, type: "undirected" })
  const segs = new Map<string, Seg>()

  const resolveNode = (c: Coord): string => {
    for (const k of graph.nodes()) if (dm(c, fromNk(k)) <= CONNECT_M) return k
    const k = nk(c)
    graph.addNode(k)
    return k
  }

  // ── Build spatial grid ────────────────────────────────────────────────────
  interface RoadEntry {
    road: LayerEntry
    coords: Coord[]
    minLat: number
    maxLat: number
    minLng: number
    maxLng: number
    endpoints: Coord[]
  }

  const roadEntries: RoadEntry[] = []
  const grid = new Map<string, RoadEntry[]>()

  for (const road of roads) {
    const coords = road.data.coordinates
    if (!coords?.length) continue

    let minLat = Infinity, maxLat = -Infinity, minLng = Infinity, maxLng = -Infinity
    for (const c of coords) {
      if (c.lat < minLat) minLat = c.lat
      if (c.lat > maxLat) maxLat = c.lat
      if (c.lng < minLng) minLng = c.lng
      if (c.lng > maxLng) maxLng = c.lng
    }

    const entry: RoadEntry = {
      road,
      coords,
      minLat,
      maxLat,
      minLng,
      maxLng,
      endpoints: [coords[0], coords[coords.length - 1]],
    }
    roadEntries.push(entry)

    for (const ck of cellsForBbox(minLat, maxLat, minLng, maxLng)) {
      let bucket = grid.get(ck)
      if (!bucket) {
        bucket = []
        grid.set(ck, bucket)
      }
      bucket.push(entry)
    }
  }

  // ── Detect junctions using grid neighbors only ─────────────────────────────
  for (const re of roadEntries) {
    const { road, coords } = re
    const dbId = road.dbId
    const line = toLn(coords)

    // Collect candidate neighbors from all cells this road touches
    const neighborSet = new Set<RoadEntry>()
    for (const ck of cellsForBbox(re.minLat, re.maxLat, re.minLng, re.maxLng)) {
      for (const n of grid.get(ck) ?? []) {
        if (n !== re) neighborSet.add(n)
      }
    }

    const junctions: Array<{ segIdx: number; pt: Coord }> = []

    for (const other of neighborSet) {
      const otherCoords = other.coords
      const otherLine = toLn(otherCoords)

      // ── T-junction: endpoint of `other` lands on body of `road` ─────
      for (const ep of other.endpoints) {
        if (dm(ep, coords[0]) <= CONNECT_M) continue
        if (dm(ep, coords[coords.length - 1]) <= CONNECT_M) continue

        const nearest = turfNearest.nearestPointOnLine(line, toPt(ep), {
          units: "meters",
        })
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
      const intersections = turfLineIntersect.lineIntersect(line, otherLine)
      for (const feature of intersections.features) {
        const pt: Coord = {
          lat: feature.geometry.coordinates[1],
          lng: feature.geometry.coordinates[0],
        }
        if (dm(pt, coords[0]) <= CONNECT_M) continue
        if (dm(pt, coords[coords.length - 1]) <= CONNECT_M) continue

        const nearest = turfNearest.nearestPointOnLine(line, toPt(pt), {
          units: "meters",
        })
        junctions.push({
          segIdx: nearest.properties.index ?? 0,
          pt,
        })
      }
    }

    // ── Slice road into segments at junction points ──────────────────────
    junctions.sort((a, b) => a.segIdx - b.segIdx)

    const splitPts: Array<{ segIdx: number; pt: Coord }> = []
    for (const j of junctions) {
      const last = splitPts[splitPts.length - 1]
      if (!last || dm(j.pt, last.pt) > CONNECT_M) splitPts.push(j)
    }

    const slices: Coord[][] = []
    let prev = 0

    for (const { segIdx, pt } of splitPts) {
      if (segIdx <= prev) continue
      slices.push([...coords.slice(prev, segIdx + 1), pt])
      prev = segIdx
    }
    slices.push([...coords.slice(prev)])

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

export type { Coord }
