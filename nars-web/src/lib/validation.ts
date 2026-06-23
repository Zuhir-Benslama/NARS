import { apiFetch } from "../api"
import { NarsError, logError } from "./errors"
import { VALIDATION_CONFIG } from "../config"
import { debugError } from "../utils/debug"
import type {
  ValidateRoadResponse,
  ValidateDistrictResponse,
  DistrictCoverageResponse,
  RoadSideResponse,
  LatLng,
} from "../types"

const MIN_ROAD_LENGTH_M = VALIDATION_CONFIG.minRoadLengthMeters

// POST /api/validate/road
export async function validateRoad(coordinates: LatLng[]): Promise<ValidateRoadResponse> {
  // Client-side minimum length check
  if (coordinates.length >= 2) {
    try {
      const [turfHelpers, turfLength] = await Promise.all([
        import("@turf/helpers"),
        import("@turf/length"),
      ])
      const line = turfHelpers.lineString(coordinates.map((c) => [c.lng, c.lat]))
      const metres = turfLength.length(line, { units: "meters" })
      if (metres < MIN_ROAD_LENGTH_M) {
        return {
          valid: false,
          error: `Road is too short (${metres.toFixed(1)} m). Minimum length is ${MIN_ROAD_LENGTH_M} m.`,
        }
      }
    } catch {
      // Turf import failed — skip client-side check, let server validate
    }
  }

  try {
    const res = await apiFetch("/api/validate/road", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ coordinates }),
    })
    return (await res.json()) as ValidateRoadResponse
  } catch (err) {
    if (err instanceof NarsError) {
      logError(err, { action: "validateRoad" })
      return { valid: false, error: err.message }
    }
    if (err instanceof TypeError) {
      debugError("[VALIDATION] validateRoad network error:", err)
      return { valid: false, error: "Cannot reach validation service." }
    }
    debugError("[VALIDATION] validateRoad unexpected error:", err)
    return { valid: false, error: "Road validation encountered an unexpected error." }
  }
}

// POST /api/validate/district
export async function validateDistrict(
  coordinates: LatLng[],
  districtTypeKey?: string,
): Promise<ValidateDistrictResponse> {
  // PostGIS requires closed ring — ensure first and last points are identical
  let coords = [...coordinates]
  if (coords.length >= 3) {
    const first = coords[0],
      last = coords[coords.length - 1]
    if (first.lat !== last.lat || first.lng !== last.lng) {
      coords = [...coords, { lat: first.lat, lng: first.lng }]
    }
  }
  try {
    const res = await apiFetch("/api/validate/district", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ coordinates: coords, districtTypeKey }),
    })
    return (await res.json()) as ValidateDistrictResponse
  } catch (err) {
    if (err instanceof NarsError) {
      logError(err, { action: "validateDistrict" })
      return { valid: false, error: err.message }
    }
    debugError("[VALIDATION] validateDistrict network error:", err)
    return { valid: false, error: "Cannot reach validation service." }
  }
}

// GET /api/validate/districts/coverage
export async function checkDistrictCoverage(): Promise<DistrictCoverageResponse> {
  try {
    const res = await apiFetch("/api/validate/districts/coverage")
    return (await res.json()) as DistrictCoverageResponse
  } catch (err) {
    if (err instanceof NarsError) {
      logError(err, { action: "checkDistrictCoverage" })
      return { covered: false, message: err.message }
    }
    debugError("[VALIDATION] checkDistrictCoverage network error:", err)
    return { covered: false, message: "Cannot reach validation service." }
  }
}

// GET /api/validate/area/main-urban-exists
export async function checkMainUrbanExists(): Promise<boolean> {
  try {
    const res = await apiFetch("/api/validate/area/main-urban-exists")
    const d = (await res.json()) as { exists: boolean }
    return d.exists
  } catch (err) {
    debugError("checkMainUrbanExists failed:", err)
    return false
  }
}

// POST /api/road-side → { side: 'left'|'right', suggestedNumber: number }
export async function getRoadSide(
  roadDbId: string,
  lat: number,
  lng: number,
): Promise<RoadSideResponse | null> {
  try {
    const res = await apiFetch("/api/road-side", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ roadId: roadDbId, lat, lng }),
    })
    return (await res.json()) as RoadSideResponse
  } catch (err) {
    debugError("getRoadSide failed:", err)
    return null
  }
}
