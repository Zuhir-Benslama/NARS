// ─── CITY CENTER RULES ────────────────────────────────────────────────────────
// Shared rules for draw/edit placement checks and the modal's radius check:
// radius limits, and the "0 or 1 plain city center per urban area" placement
// rule. Roundabouts are a different city-center layer and are not capped.

import type { FeatureData, LatLng } from "../types"
import { CITY_CENTER_CONFIG } from "../config"

export type CityCenterRadiusError = "too_small" | "too_large" | null
export type PlainCityCenterPlacementError =
  "outside_urban_area" | "urban_area_has_city_center" | null

interface PlacementFeature {
  data: FeatureData
  dbId?: string
  id?: string
}

const URBAN_AREA_LAYERS = new Set(["central_urban", "secondary_urban"])

export function cityCenterRadiusError(radius: number | undefined | null): CityCenterRadiusError {
  if (!radius || Number.isNaN(radius) || radius < CITY_CENTER_CONFIG.minRadiusM) {
    return "too_small"
  }
  if (radius > CITY_CENTER_CONFIG.maxRadiusM) {
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

function isPlainCityCenter(feature: PlacementFeature): boolean {
  return (feature.data.cityCenterTypeKey ?? "city_center") !== "roundabout"
}

/**
 * A plain city center is optional (0 or 1) inside each urban area polygon, and
 * must be placed inside one. Passing `ignoreId` excludes the feature currently
 * being edited from the occupancy check.
 */
export function plainCityCenterPlacementError(
  point: Partial<LatLng> | null | undefined,
  areas: readonly PlacementFeature[],
  cityCenters: readonly PlacementFeature[],
  ignoreId?: string,
): PlainCityCenterPlacementError {
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

  const occupied = cityCenters.some((center) => {
    if (ignoreId && (center.dbId === ignoreId || center.id === ignoreId)) return false
    if (!isPlainCityCenter(center)) return false
    if (center.data.lat == null || center.data.lng == null) return false
    return pointInRing(
      { lat: center.data.lat, lng: center.data.lng },
      (area.data.coordinates ?? []) as LatLng[],
    )
  })

  return occupied ? "urban_area_has_city_center" : null
}
