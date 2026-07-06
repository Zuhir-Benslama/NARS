// ─── ROAD GRAPH ───────────────────────────────────────────────────────────────
// Phase 1: Connection scan builds a full topology graph from road coordinates.
// Handles endpoint-to-endpoint, endpoint-to-body (T-junctions),
// and body-to-body (X-junctions / crossings).

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

  for (const road of roads) {
    const coords = road.data.coordinates
    if (!coords?.length) continue
    const dbId = road.dbId
    const line = toLn(coords)

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
      const intersects = turfLineIntersect.lineIntersect(line, otherLine)
      for (const feature of intersects.features) {
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
