// ─── SNAP GEOMETRY HELPERS ────────────────────────────────────────────────────
// Pure, stateless geometry functions used by the snap engine (snapping.ts).
// No access to module state, featureLayers, or ctx — only math.
// Extracted from snapping.ts to keep that file focused on the state machine.

import type maplibregl from "maplibre-gl"

/** The result of map.project() — pixel coordinates with .x and .y */
type PixelPoint = { x: number; y: number }

// ─── PRIMITIVES ───────────────────────────────────────────────────────────────

/**
 * Closest point on segment [a, b] to cursor point (cursorX, cursorY) in pixel space.
 * Returns pixel coordinates of the closest point, or null if segment is degenerate.
 */
export function closestOnSegment(
  cursorX: number,
  cursorY: number,
  aLng: number,
  aLat: number,
  bLng: number,
  bLat: number,
  project: (ll: [number, number]) => PixelPoint,
  unproject: (pt: [number, number]) => maplibregl.LngLat,
): { x: number; y: number; lng: number; lat: number } | null {
  try {
    const pa = project([aLng, aLat]) as PixelPoint
    const pb = project([bLng, bLat]) as PixelPoint
    const dx = pb.x - pa.x,
      dy = pb.y - pa.y
    const lenSq = dx * dx + dy * dy
    if (lenSq === 0) return { x: pa.x, y: pa.y, lng: aLng, lat: aLat }
    const t = Math.max(0, Math.min(1, ((cursorX - pa.x) * dx + (cursorY - pa.y) * dy) / lenSq))
    const ex = pa.x + t * dx,
      ey = pa.y + t * dy
    const ll = unproject([ex, ey])
    return { x: ex, y: ey, lng: ll.lng, lat: ll.lat }
  } catch {
    return null
  }
}

/**
 * Closest point on a circle's visual perimeter (pixel space).
 * The circle is defined by center (centerLng, centerLat) and radius in meters.
 */
export function closestOnCirclePerimeter(
  cursorX: number,
  cursorY: number,
  centerLng: number,
  centerLat: number,
  radiusMeters: number,
  project: (ll: [number, number]) => PixelPoint,
  unproject: (pt: [number, number]) => maplibregl.LngLat,
): { x: number; y: number; lng: number; lat: number; dist: number } | null {
  try {
    const centerPx = project([centerLng, centerLat]) as PixelPoint
    // Approximate radius in pixels by projecting a 0.001° offset east.
    // 111320 = meters per degree of longitude at the equator (Earth circumference / 360).
    // The cosine factor scales this for the current latitude (Mercator projection).
    // Note: This is an approximation. At extreme latitudes (>75°) or very high zoom
    // levels the distortion may cause slight inaccuracies in circle perimeter snapping.
    const offsetPt = project([centerLng + 0.001, centerLat]) as PixelPoint
    const metersPerPixelX =
      (0.001 * 111320 * Math.cos((centerLat * Math.PI) / 180)) / (offsetPt.x - centerPx.x)
    const radiusPx = radiusMeters / metersPerPixelX
    if (radiusPx === 0 || !isFinite(radiusPx)) return null

    const dx = cursorX - centerPx.x,
      dy = cursorY - centerPx.y
    const cursorDist = Math.hypot(dx, dy)
    if (cursorDist === 0) return null
    const snapX = centerPx.x + (dx / cursorDist) * radiusPx
    const snapY = centerPx.y + (dy / cursorDist) * radiusPx
    const snapLL = unproject([snapX, snapY])
    return {
      x: snapX,
      y: snapY,
      lng: snapLL.lng,
      lat: snapLL.lat,
      dist: Math.abs(cursorDist - radiusPx),
    }
  } catch {
    return null
  }
}

/** Pixel distance from cursor point to a map coordinate. Returns null on projection failure. */
export function pixelDist(
  cursorX: number,
  cursorY: number,
  lng: number,
  lat: number,
  project: (ll: [number, number]) => PixelPoint,
): number | null {
  try {
    const p = project([lng, lat]) as PixelPoint
    return Math.hypot(p.x - cursorX, p.y - cursorY)
  } catch {
    return null
  }
}

/**
 * Closest point on a segment defined by pre-projected pixel endpoints.
 * This avoids redundant project() calls inside the snap search loop.
 */
export function closestOnSegmentProjected(
  cursorX: number,
  cursorY: number,
  ax: number,
  ay: number,
  bx: number,
  by: number,
  alat: number,
  alng: number,
  unproject: (pt: [number, number]) => maplibregl.LngLat,
): { x: number; y: number; lng: number; lat: number } | null {
  try {
    const dx = bx - ax,
      dy = by - ay
    const lenSq = dx * dx + dy * dy
    if (lenSq === 0) return { x: ax, y: ay, lng: alng, lat: alat }
    const t = Math.max(0, Math.min(1, ((cursorX - ax) * dx + (cursorY - ay) * dy) / lenSq))
    const ex = ax + t * dx,
      ey = ay + t * dy
    const ll = unproject([ex, ey])
    return { x: ex, y: ey, lng: ll.lng, lat: ll.lat }
  } catch {
    return null
  }
}
