// ─── GEOMETRY, BOUNDARY & SCATTERED AREAS ────────────────────────────────────
// Shared geometry computation functions used across draw-complete, loader,
// edit-mode, and undo modules.

import { ctx } from "../core/state"
import { apiFetch } from "../../api"
import { GEOMETRY_CONFIG } from "../../config"
import { debugError } from "../../utils/debug"
import maplibregl from "maplibre-gl"
import type { ScatteredRefreshResponse } from "../../types"

// ─── MUNICIPALITY BOUNDARY ────────────────────────────────────────────────────

let municipalLimitRings: number[][][] = []
if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    municipalLimitRings = []
    scatteredPolygons = []
  })
}

function pointInRing(lat: number, lng: number, ring: number[][]): boolean {
  let inside = false
  const x = lat,
    y = lng
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const xi = ring[i][0],
      yi = ring[i][1]
    const xj = ring[j][0],
      yj = ring[j][1]
    if (yi > y !== yj > y && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi) inside = !inside
  }
  return inside
}

export function pointInMunicipalLimit(lat: number, lng: number): boolean {
  if (municipalLimitRings.length === 0) return true
  return municipalLimitRings.some((r) => pointInRing(lat, lng, r))
}

// ─── SCATTERED AREAS ──────────────────────────────────────────────────────────

interface ScatteredPoly {
  outer: [number, number][]
  holes: [number, number][][]
}
let scatteredPolygons: ScatteredPoly[] = []

export function pointInScatteredArea(lat: number, lng: number): boolean {
  return scatteredPolygons.some(
    ({ outer, holes }) =>
      pointInRing(lat, lng, outer) && !holes.some((h) => pointInRing(lat, lng, h)),
  )
}

export function renderScatteredAreas(geoJsonStr: string | GeoJSON.Geometry): void {
  scatteredPolygons = []
  if (!geoJsonStr) return
  try {
    const geojson: GeoJSON.Geometry =
      typeof geoJsonStr === "string" ? JSON.parse(geoJsonStr) : geoJsonStr
    if (!geojson?.type) return

    // Extract scattered polygons for spatial hit-testing
    if (geojson.type === "Polygon") {
      const coords = (geojson as GeoJSON.Polygon).coordinates
      scatteredPolygons.push({
        outer: coords[0] as [number, number][],
        holes: coords.slice(1) as [number, number][][],
      })
    } else if (geojson.type === "MultiPolygon") {
      const mp = geojson as GeoJSON.MultiPolygon
      for (const poly of mp.coordinates) {
        scatteredPolygons.push({
          outer: poly[0] as [number, number][],
          holes: poly.slice(1) as [number, number][][],
        })
      }
    }
  } catch (e) {
    debugError("Scattered render error:", e)
  }
}

export async function displayCommuneBoundary(communeId: number): Promise<void> {
  try {
    const data = (await apiFetch(`/api/commune/${communeId}/boundary`).then((r) => r.json())) as {
      geometry: string | GeoJSON.Geometry
      commune_name?: string
    }
    const geojson: GeoJSON.Geometry =
      typeof data.geometry === "string" ? JSON.parse(data.geometry) : data.geometry
    if (!geojson?.type) return

    // Extract rings for point-in-polygon hit testing
    municipalLimitRings = []
    if (geojson.type === "Polygon") {
      municipalLimitRings.push(...(geojson as GeoJSON.Polygon).coordinates)
    } else if (geojson.type === "MultiPolygon") {
      const mp = geojson as GeoJSON.MultiPolygon
      for (const poly of mp.coordinates) {
        municipalLimitRings.push(...poly)
      }
    }

    // Update the boundaries GeoJSON source
    if (ctx.boundariesSource) {
      ctx.boundariesSource.setData({
        type: "FeatureCollection",
        features: [
          {
            type: "Feature",
            geometry: geojson,
            properties: {},
          },
        ],
      })
    }

    // Fly camera to frame the commune boundary
    if (ctx.map) {
      const bounds = computeBoundsFromGeometry(geojson)
      if (bounds) {
        ctx.map.fitBounds(bounds, {
          padding: 60,
          maxZoom: 14,
          duration: 1500,
          essential: true,
        })
      }
    }
  } catch (e) {
    debugError("Boundary error:", e)
  }
}

/** Compute a maplibregl.LngLatBounds from any GeoJSON geometry. Returns null if no coords found. */
function computeBoundsFromGeometry(geojson: GeoJSON.Geometry): maplibregl.LngLatBoundsLike | null {
  const bounds = new maplibregl.LngLatBounds()
  let hasCoords = false

  const collectCoords = (coords: unknown) => {
    if (Array.isArray(coords) && coords.length >= 2 && typeof coords[0] === "number") {
      bounds.extend(coords as [number, number])
      hasCoords = true
    } else if (Array.isArray(coords)) {
      for (const c of coords) collectCoords(c)
    }
  }

  if (geojson.type === "Polygon") {
    collectCoords((geojson as GeoJSON.Polygon).coordinates)
  } else if (geojson.type === "MultiPolygon") {
    collectCoords((geojson as GeoJSON.MultiPolygon).coordinates)
  } else if (geojson.type === "Point") {
    const c = (geojson as GeoJSON.Point).coordinates
    bounds.extend(c as [number, number])
    hasCoords = true
  } else if (geojson.type === "LineString") {
    collectCoords((geojson as GeoJSON.LineString).coordinates)
  }

  return hasCoords ? bounds : null
}

export async function refreshScatteredAreas(): Promise<void> {
  try {
    const data = (await apiFetch("/api/areas/refresh-scattered", {
      method: "POST",
    }).then((r) => r.json())) as ScatteredRefreshResponse
    if (data.geojson) renderScatteredAreas(data.geojson)
  } catch (e) {
    debugError("Scatter refresh error:", e)
  }
}

// ─── CIRCLE RING COMPUTATION ─────────────────────────────────────────────────

/**
 * Compute a circle ring (Polygon coordinates) from center + radius in meters.
 * Uses configurable segments for a smooth circle approximation.
 *
 * @param lat Center latitude in degrees
 * @param lng Center longitude in degrees
 * @param radiusMeters Radius in meters
 * @returns Array of [lng, lat] coordinates forming a closed ring
 */
export function computeCircleRing(
  lat: number,
  lng: number,
  radiusMeters: number,
): [number, number][] {
  return _computeCircleRing(lat, lng, radiusMeters, GEOMETRY_CONFIG.circleSegments)
}

/**
 * Compute a simplified circle ring for editing purposes.
 * Uses fewer segments to reduce the number of vertex handles shown.
 */
export function computeCircleRingForEdit(
  lat: number,
  lng: number,
  radiusMeters: number,
): [number, number][] {
  return _computeCircleRing(lat, lng, radiusMeters, GEOMETRY_CONFIG.editCircleSegments)
}

function _computeCircleRing(
  lat: number,
  lng: number,
  radiusMeters: number,
  segments: number,
): [number, number][] {
  const R = GEOMETRY_CONFIG.earthRadiusMeters
  const ring: [number, number][] = []
  const latRad = (lat * Math.PI) / 180
  const lngRad = (lng * Math.PI) / 180
  const angularDist = radiusMeters / R

  for (let i = 0; i < segments; i++) {
    const bearing = (2 * Math.PI * i) / segments
    const newLat = Math.asin(
      Math.sin(latRad) * Math.cos(angularDist) +
        Math.cos(latRad) * Math.sin(angularDist) * Math.cos(bearing),
    )
    const newLng =
      lngRad +
      Math.atan2(
        Math.sin(bearing) * Math.sin(angularDist) * Math.cos(latRad),
        Math.cos(angularDist) - Math.sin(latRad) * Math.sin(newLat),
      )
    ring.push([(newLng * 180) / Math.PI, (newLat * 180) / Math.PI])
  }
  // Close the ring — first and last point must be identical for a valid GeoJSON Polygon
  ring.push([ring[0][0], ring[0][1]])
  return ring
}

// ─── SHARED GEOMETRY UTILITIES ────────────────────────────────────────────────

/**
 * Ensure a linear ring is closed by appending the first point if needed.
 * Used across draw-complete, edit-mode, context-menu, undo, and loader.
 *
 * @param ring Array of [lng, lat] coordinates
 * @returns The same array, guaranteed closed (first === last)
 */
export function closeRing(ring: [number, number][]): [number, number][] {
  if (ring.length < 2) return ring
  const first = ring[0]
  const last = ring[ring.length - 1]
  if (first[0] !== last[0] || first[1] !== last[1]) {
    ring.push([first[0], first[1]])
  }
  return ring
}

/**
 * Compute the Haversine distance between two lat/lng points in meters.
 */
export function haversineDistance(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = GEOMETRY_CONFIG.earthRadiusMeters
  const toRad = (deg: number) => (deg * Math.PI) / 180
  const dLat = toRad(lat2 - lat1)
  const dLng = toRad(lng2 - lng1)
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2
  return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
}

/**
 * Compute the average radius of a circle from its center and polygon ring.
 * Replaces duplicated Haversine-average-radius computation in draw-events,
 * draw-complete, edit-mode, and loader.
 *
 * @param centerLat Center latitude
 * @param centerLng Center longitude
 * @param ring Closed ring of [lng, lat] coordinates
 * @returns Average radius in meters
 */
export function computeCircleRadius(
  centerLat: number,
  centerLng: number,
  ring: [number, number][],
): number {
  const total = ring.reduce(
    (sum, [lng, lat]) => sum + haversineDistance(centerLat, centerLng, lat, lng),
    0,
  )
  return ring.length > 0 ? total / ring.length : 0
}

/**
 * Build a GeoJSON Feature object from feature data.
 * Used across loader, undo, and draw-complete.
 */
export function makeGeoJsonFeature(
  geometry: GeoJSON.Geometry,
  properties: Record<string, unknown>,
): GeoJSON.Feature {
  return { type: "Feature", geometry, properties }
}
