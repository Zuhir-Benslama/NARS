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

export const CONNECT_M = 30 // Junction detection threshold in meters

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
 *
 * Buckets are keyed in METERS, not degrees: a degree-sized cell (60°) would
 * collapse a whole country into a handful of cells and silently defeat the
 * spatial index, degrading junction detection back to O(n²) turf geometry
 * comparisons for every pair of roads.
 */
const CELL_SIZE = CONNECT_M * 2 // meters
// ~WGS84 mean meters per degree of latitude.
const METERS_PER_DEG_LAT = 111_320

function latCell(lat: number): number {
  return Math.floor((lat * METERS_PER_DEG_LAT) / CELL_SIZE)
}

/**
 * Longitude meters-per-degree shrink with latitude (× cos(lat)). `lngScale`
 * is the dataset's minimum cos (most equator-ward latitude), so lng cells
 * are never smaller than CELL_SIZE in real meters and the 3×3-neighbor
 * lookup below still covers every junction within CONNECT_M.
 */
function lngCell(lng: number, lngScale: number): number {
  return Math.floor((lng * METERS_PER_DEG_LAT * lngScale) / CELL_SIZE)
}

/** Expand a bounding box into the set of grid cells it overlaps. */
function cellsForBbox(
  minLat: number,
  maxLat: number,
  minLng: number,
  maxLng: number,
  lngScale: number,
): Set<string> {
  const cells = new Set<string>()
  const r0 = latCell(minLat)
  const r1 = latCell(maxLat)
  const c0 = lngCell(minLng, lngScale)
  const c1 = lngCell(maxLng, lngScale)
  for (let r = r0; r <= r1; r++) {
    for (let c = c0; c <= c1; c++) {
      cells.add(`${r},${c}`)
    }
  }
  return cells
}

/** Global lng scale (min cos over the dataset) so cells are never under-sized. */
function computeLngScale(roads: LayerEntry[]): number {
  let scale = 1
  for (const road of roads) {
    for (const c of road.data.coordinates ?? []) {
      const cos = Math.cos(Math.abs(c.lat) * (Math.PI / 180))
      if (cos < scale) scale = cos
    }
  }
  return scale
}

// ─── PHASE 1: CONNECTION SCAN → GRAPH ────────────────────────────────────────

export function buildConnectionGraph(roads: LayerEntry[]): {
  graph: Graph
  segs: Map<string, Seg>
} {
  const graph = new Graph({ multi: true, type: "undirected" })
  const segs = new Map<string, Seg>()
  const lngScale = computeLngScale(roads)

  // Spatial index for graph nodes — avoids O(n) scan per resolveNode call
  const nodeGrid = new Map<string, Set<string>>()

  const resolveNode = (c: Coord): string => {
    const cr = latCell(c.lat)
    const cc = lngCell(c.lng, lngScale)
    // Check self cell + 8 neighbors (cell size = 2×CONNECT_M, so this covers all matches within CONNECT_M)
    for (let r = cr - 1; r <= cr + 1; r++) {
      for (let c2 = cc - 1; c2 <= cc + 1; c2++) {
        const cell = nodeGrid.get(`${r},${c2}`)
        if (!cell) continue
        for (const k of cell) {
          if (dm(c, fromNk(k)) <= CONNECT_M) return k
        }
      }
    }
    const k = nk(c)
    graph.addNode(k)
    const ck = `${cr},${cc}`
    let bucket = nodeGrid.get(ck)
    if (!bucket) {
      bucket = new Set()
      nodeGrid.set(ck, bucket)
    }
    bucket.add(k)
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

    let minLat = Infinity,
      maxLat = -Infinity,
      minLng = Infinity,
      maxLng = -Infinity
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

    for (const ck of cellsForBbox(minLat, maxLat, minLng, maxLng, lngScale)) {
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
    for (const ck of cellsForBbox(re.minLat, re.maxLat, re.minLng, re.maxLng, lngScale)) {
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
