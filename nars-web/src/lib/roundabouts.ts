// ─── ROUNDABOUTS RULES ────────────────────────────────────────────────────────
// Shared rules for draw/edit placement checks and the modal's radius check:
// radius limits, and the "0 or 1 plain city center per urban area" placement
// rule. The plain city center is a subtype of roundabouts and is capped;
// true roundabouts are not.

import type { FeatureData, LatLng } from "../types"
import { ROUNDABOUTS_CONFIG } from "../config"

export type RoundaboutsRadiusError = "too_small" | "too_large" | null
export type PlainRoundaboutsPlacementError =
  "outside_urban_area" | "urban_area_has_plain_roundabouts" | null

interface PlacementFeature {
  data: FeatureData
  dbId?: string
  id?: string
}

const URBAN_AREA_LAYERS = new Set(["central_urban", "secondary_urban"])

/** Center point of a drawn roundabouts circle (point or polygon geometry). */
export function roundaboutsPointFromGeometry(geometry: GeoJSON.Geometry): LatLng | null {
  if (geometry.type === "Point") {
    const [lng, lat] = geometry.coordinates as [number, number]
    return Number.isFinite(lng) && Number.isFinite(lat) ? { lat, lng } : null
  }
  if (geometry.type === "Polygon") {
    const ring = (geometry.coordinates as [number, number][][])[0] ?? []
    if (ring.length < 3) return null
    // Closing vertex is omitted so it does not skew the centroid.
    const vertices =
      ring.length > 1 &&
      ring[0][0] === ring[ring.length - 1][0] &&
      ring[0][1] === ring[ring.length - 1][1]
        ? ring.slice(0, -1)
        : ring
    const lat = vertices.reduce((sum, c) => sum + c[1], 0) / vertices.length
    const lng = vertices.reduce((sum, c) => sum + c[0], 0) / vertices.length
    return Number.isFinite(lat) && Number.isFinite(lng) ? { lat, lng } : null
  }
  return null
}

export function roundaboutsRadiusError(radius: number | undefined | null): RoundaboutsRadiusError {
  if (!radius || Number.isNaN(radius) || radius < ROUNDABOUTS_CONFIG.minRadiusM) {
    return "too_small"
  }
  if (radius > ROUNDABOUTS_CONFIG.maxRadiusM) {
    return "too_large"
  }
  return null
}

function pointInRing(point: LatLng, ring: LatLng[]): boolean {
  if (ring.length < 3) return false
  let inside = false
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const yi = ring[i].lat
    const yj = ring[j].lat
    if (yi > point.lat !== yj > point.lat) {
      const edgeLng =
        ring[i].lng + ((ring[j].lng - ring[i].lng) * (point.lat - yi)) / (ring[j].lat - ring[i].lat)
      if (point.lng < edgeLng) inside = !inside
    }
  }
  return inside
}

function isUrbanArea(feature: PlacementFeature): boolean {
  return URBAN_AREA_LAYERS.has(feature.data.areaTypeKey ?? "")
}

/**
 * Reads the roundabouts subtype. Rows created before the collapse stored it as
 * `cityCenterTypeKey`, so that spelling is accepted until every row is migrated.
 */
export function roundaboutsSubtype(
  data: Pick<FeatureData, "roundaboutsTypeKey" | "cityCenterTypeKey"> | null | undefined,
): string {
  return data?.roundaboutsTypeKey ?? data?.cityCenterTypeKey ?? "city_center"
}

export function isPlainRoundabouts(
  data: Pick<FeatureData, "roundaboutsTypeKey" | "cityCenterTypeKey"> | null | undefined,
): boolean {
  return roundaboutsSubtype(data) !== "roundabout"
}

/**
 * A plain city center is optional (0 or 1) inside each urban area polygon, and
 * must be placed inside one. Passing `ignoreId` excludes the feature currently
 * being edited from the occupancy check.
 */
export function plainRoundaboutsPlacementError(
  point: Partial<LatLng> | null | undefined,
  areas: readonly PlacementFeature[],
  roundabouts: readonly PlacementFeature[],
  ignoreId?: string,
): PlainRoundaboutsPlacementError {
  const lat = point?.lat
  const lng = point?.lng
  if (
    typeof lat !== "number" ||
    typeof lng !== "number" ||
    !Number.isFinite(lat) ||
    !Number.isFinite(lng)
  ) {
    return "outside_urban_area"
  }
  const center = { lat, lng }

  const area = areas.find(
    (candidate) =>
      isUrbanArea(candidate) && pointInRing(center, (candidate.data.coordinates ?? []) as LatLng[]),
  )
  if (!area) return "outside_urban_area"

  const occupied = roundabouts.some((center) => {
    if (ignoreId && (center.dbId === ignoreId || center.id === ignoreId)) return false
    if (!isPlainRoundabouts(center.data)) return false
    if (center.data.lat == null || center.data.lng == null) return false
    return pointInRing(
      { lat: center.data.lat, lng: center.data.lng },
      (area.data.coordinates ?? []) as LatLng[],
    )
  })

  return occupied ? "urban_area_has_plain_roundabouts" : null
}
