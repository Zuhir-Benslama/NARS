// ─── SNAP SEARCH ──────────────────────────────────────────────────────────────
// Finds the nearest snap target (circle, vertex, midpoint, edge) to the cursor.
// Snap priority: circle → vertex → midpoint → edge.

import { getCtx } from "../core/state"
import { SNAP_CONFIG } from "../../config"
import { closestOnSegmentProjected, closestOnCirclePerimeter, pixelDist } from "./snap-geometry"
import { getSnapRings, getRoadChains, getCityCenterCircles, getSnapPoints } from "./snap-sources"

// ─── THRESHOLDS ───────────────────────────────────────────────────────────────

const CORNER_PX = SNAP_CONFIG.thresholds.vertex
const EDGE_PX = SNAP_CONFIG.thresholds.edge
const CIRCLE_PX = SNAP_CONFIG.thresholds.circle
const MIDPOINT_PX = SNAP_CONFIG.thresholds.midpoint

// ─── TYPES ────────────────────────────────────────────────────────────────────

type SnapType = "vertex" | "midpoint" | "edge" | "circle"

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

// ─── FIRST VERTEX SNAP ────────────────────────────────────────────────────────

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
  const gm = getCtx().geoman as
    | {
        actionInstances?: Record<string, { lineDrawer?: { shapeLngLats?: [number, number][] } }>
      }
    | undefined
  const ld =
    gm?.actionInstances?.draw__polygon?.lineDrawer ?? gm?.actionInstances?.draw__line?.lineDrawer
  const sh = ld?.shapeLngLats
  if (!sh?.length) return external

  const first = sh[0]
  const dFirst = pixelDist(cursorX, cursorY, first[0], first[1], project)
  if (dFirst === null || dFirst >= CORNER_PX) return external

  const firstSnap: SnapResult = {
    lng: first[0],
    lat: first[1],
    type: "vertex",
  }
  if (!external) return firstSnap
  const dExt = pixelDist(cursorX, cursorY, external.lng, external.lat, project)
  return dFirst <= (dExt ?? Infinity) ? firstSnap : external
}

// ─── NEAREST SNAP SEARCH ──────────────────────────────────────────────────────

export function findNearestSnap(
  cursorX: number,
  cursorY: number,
  phaseKeys: string[],
  includeMidpoint: boolean,
  excludeId?: string | null,
): SnapResult | null {
  const { map } = getCtx()
  const best: {
    vertex: SnapCandidate | null
    midpoint: SnapCandidate | null
    edge: SnapCandidate | null
    circle: SnapCandidate | null
  } = { vertex: null, midpoint: null, edge: null, circle: null }

  const project = (ll: [number, number]) => map.project(ll)
  const unproject = (pt: [number, number]) => map.unproject(pt)

  // ── Viewport culling ────────────────────────────────────────────
  const bounds = map.getBounds()
  const CULL_PAD = 0.03
  const inCullBox = (lat: number, lng: number): boolean =>
    lat >= bounds.getSouth() - CULL_PAD &&
    lat <= bounds.getNorth() + CULL_PAD &&
    lng >= bounds.getWest() - CULL_PAD &&
    lng <= bounds.getEast() + CULL_PAD

  // ── Pre-project all snap vertices once ──────────────────────────
  const projectedVertices: ProjectedVertex[] = []
  const projectedSegments: ProjectedSegment[] = []

  const addRing = (ring: { lat: number; lng: number }[]): void => {
    for (let i = 0; i < ring.length; i++) {
      const v = ring[i]
      const p = project([v.lng, v.lat])
      if (inCullBox(v.lat, v.lng)) {
        projectedVertices.push({ lat: v.lat, lng: v.lng, px: p.x, py: p.y })
      }
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

  for (const ring of getSnapRings(phaseKeys, excludeId)) addRing(ring)
  for (const chain of getRoadChains(phaseKeys, excludeId)) addChain(chain)

  const snapPoints = getSnapPoints(phaseKeys, excludeId)
  const projectedPoints: ProjectedVertex[] = []
  for (const pt of snapPoints) {
    if (!inCullBox(pt.lat, pt.lng)) continue
    const p = project([pt.lng, pt.lat])
    projectedPoints.push({ lat: pt.lat, lng: pt.lng, px: p.x, py: p.y })
  }

  // ── Circle perimeters ───────────────────────────────────────────
  const circles = getCityCenterCircles(phaseKeys, excludeId)
  for (const c of circles) {
    const cp = closestOnCirclePerimeter(
      cursorX,
      cursorY,
      c.lng,
      c.lat,
      c.radius,
      project,
      unproject,
    )
    if (cp && cp.dist < CIRCLE_PX && (!best.circle || cp.dist < best.circle.dist)) {
      best.circle = { lat: cp.lat, lng: cp.lng, dist: cp.dist }
    }
  }

  // ── Vertices ────────────────────────────────────────────────────
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

  // ── Midpoints and edges ─────────────────────────────────────────
  if (includeMidpoint) {
    for (const s of projectedSegments) {
      const midPx = (s.ax + s.bx) / 2,
        midPy = (s.ay + s.by) / 2
      const midLL = unproject([midPx, midPy])
      const dm = Math.hypot(midPx - cursorX, midPy - cursorY)
      if (dm < MIDPOINT_PX && (!best.midpoint || dm < best.midpoint.dist)) {
        best.midpoint = { lat: midLL.lat, lng: midLL.lng, dist: dm }
      }
    }
  }

  for (const s of projectedSegments) {
    const cp = closestOnSegmentProjected(
      cursorX,
      cursorY,
      s.ax,
      s.ay,
      s.bx,
      s.by,
      s.alat,
      s.alng,
      unproject,
    )
    if (cp) {
      const de = Math.hypot(cp.x - cursorX, cp.y - cursorY)
      if (de < EDGE_PX && (!best.edge || de < best.edge.dist)) {
        best.edge = { lat: cp.lat, lng: cp.lng, dist: de }
      }
    }
  }

  // ── Priority: circle → vertex → midpoint → edge ──────────────────
  if (best.circle) return { lat: best.circle.lat, lng: best.circle.lng, type: "circle" as const }
  if (best.vertex) return { lat: best.vertex.lat, lng: best.vertex.lng, type: "vertex" as const }
  if (best.midpoint)
    return { lat: best.midpoint.lat, lng: best.midpoint.lng, type: "midpoint" as const }
  if (best.edge) return { lat: best.edge.lat, lng: best.edge.lng, type: "edge" as const }
  return null
}
