import { apiFetch } from "../api"
import { NarsError, getUserMessageKey, logError } from "./errors"
import { t } from "../i18n"
import { debugError } from "../utils/debug"
import type { DistrictCoverageResponse, RoadSideResponse } from "../types"

// GET /api/validate/districts/coverage
export async function checkDistrictCoverage(): Promise<DistrictCoverageResponse> {
  try {
    const res = await apiFetch("/api/validate/districts/coverage")
    return (await res.json()) as DistrictCoverageResponse
  } catch (err) {
    if (err instanceof NarsError) {
      logError(err, { action: "checkDistrictCoverage" })
      return { covered: false, message: t(getUserMessageKey(err)) }
    }
    debugError("[VALIDATION] checkDistrictCoverage network error:", err)
    return { covered: false, message: t("err_network") }
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
  signal?: AbortSignal,
): Promise<RoadSideResponse | null> {
  try {
    const res = await apiFetch("/api/road-side", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ roadId: roadDbId, lat, lng }),
      signal,
    })
    return (await res.json()) as RoadSideResponse
  } catch (err) {
    if (err instanceof DOMException && err.name === "AbortError") return null
    debugError("getRoadSide failed:", err)
    return null
  }
}
