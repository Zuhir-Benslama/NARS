// ─── EDIT COMMIT ──────────────────────────────────────────────────────────────
// Handles saving edited geometry to API, canceling edits with restore,
// and cleaning up Geoman state.

import { apiFetch } from "../../api"
import { getCtx } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"
import {
  computeCircleRing,
  computeCircleRadius,
  closeRing,
  computeCircleCenter,
} from "../rendering/geometry"
import { showToast } from "../../lib/toast"
import { t } from "../../i18n"
import { debugError } from "../../utils/debug"
import { PHASES } from "../../phases"
import { buildDrawControl } from "../draw/draw-control"
import { repatchMarker } from "../draw/draw-complete"
import type { LayerEntry, LatLng } from "../../types"
import {
  getActiveEditEntry,
  getActiveGeomanFeatureId,
  getActiveEditCoordsSnapshot,
  setActiveGeomanFeatureId,
  disableEditMode,
} from "./edit-state"

let _commitInProgress = false

// ─── GEOMAN FEATURE REMOVAL ──────────────────────────────────────────────────

async function removeGeomanFeature(): Promise<void> {
  const geomanId = getActiveGeomanFeatureId()
  const { geoman } = getCtx()
  if (!geoman || !geomanId) return
  try {
    await geoman.features.delete(geomanId)
  } catch {
    // Feature may already be gone
  }
  setActiveGeomanFeatureId(null)
}

// ─── GEOMETRY EXTRACTION ─────────────────────────────────────────

async function readGeomanGeometry(entry: LayerEntry): Promise<boolean> {
  if (!getActiveGeomanFeatureId() || !getCtx().geoman) return true
  const { geoman } = getCtx()
  if (!geoman) return true
  try {
    const geomanFeatures = await geoman.features.getAll()
    const geomanFeature = (
      geomanFeatures as {
        features?: Array<{ id?: string; geometry?: unknown }>
      }
    ).features?.find((f) => f.id === getActiveGeomanFeatureId())
    const rawGeometry = geomanFeature?.geometry
    if (!rawGeometry || typeof rawGeometry !== "object" || !("type" in rawGeometry)) return true

    const geometry = rawGeometry as { type: string; coordinates?: unknown }

    if (geometry.type === "LineString") {
      const lineCoords = geometry.coordinates as [number, number][] | undefined
      if (!lineCoords || lineCoords.length < 2) {
        showToast(t("map_road_min_points"), "error")
        await cancelEditMode()
        return false
      }
    }
    if (geometry.type === "Polygon") {
      const polygonCoords = geometry.coordinates as [number, number][][] | undefined
      if (!polygonCoords?.[0] || polygonCoords[0].length < 3) {
        showToast(t("map_area_min_points"), "error")
        await cancelEditMode()
        return false
      }
    }

    const d = entry.data as { lat?: number; lng?: number; radius?: number; coordinates?: LatLng[] }

    if (geometry.type === "Point") {
      const c = geometry.coordinates as [number, number]
      d.lat = c[1]
      d.lng = c[0]
    } else if (geometry.type === "Polygon") {
      const coords = (geometry.coordinates as [number, number][][])[0]
      d.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))
      if (entry.type === "circle" && coords.length >= 3) {
        const { lat, lng } = computeCircleCenter(coords)
        d.lat = lat
        d.lng = lng
        d.radius = computeCircleRadius(d.lat, d.lng, coords)
      }
    } else {
      // LineString
      const coords = geometry.coordinates as [number, number][]
      d.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))
    }
  } catch (err) {
    debugError("Failed to read Geoman geometry:", err)
  }
  return true
}

// ─── API SAVE ─────────────────────────────────────────────────────

async function saveGeometry(entry: LayerEntry): Promise<boolean> {
  try {
    await apiFetch(`/api/features/${entry.dbId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ data: entry.data }),
    })
    showToast(t("map_geometry_saved"), "success")
    return true
  } catch (err) {
    debugError("Failed to save geometry:", err)
    showToast(t("map_geometry_save_failed"), "error")
    return false
  }
}

// ─── COMMIT EDIT MODE ────────────────────────────────────────────────────────

function updateFeatureGeometry(entry: LayerEntry): void {
  const featuresStore = useFeaturesStore()
  const d = entry.data as { lat?: number; lng?: number; radius?: number; coordinates?: LatLng[] }
  if (d.lat != null && d.lng != null) {
    if (entry.type === "circle" && d.radius) {
      const ring = closeRing(computeCircleRing(d.lat, d.lng, d.radius))
      featuresStore.update(entry.id, {
        geometry: { type: "LineString", coordinates: ring },
      })
    } else {
      const geom: GeoJSON.Point = {
        type: "Point",
        coordinates: [d.lng, d.lat],
      }
      featuresStore.update(entry.id, { geometry: geom })
    }
  } else if (d.coordinates && d.coordinates.length > 0) {
    const coords = d.coordinates.map((c) => [c.lng, c.lat])
    if (entry.type === "line") {
      featuresStore.update(entry.id, {
        geometry: { type: "LineString" as const, coordinates: coords },
      })
    } else if (entry.type === "circle") {
      featuresStore.update(entry.id, {
        geometry: {
          type: "LineString" as const,
          coordinates: closeRing(coords as [number, number][]),
        },
      })
    } else {
      featuresStore.update(entry.id, {
        geometry: {
          type: "Polygon" as const,
          coordinates: [closeRing(coords as [number, number][])],
        },
      })
    }
  }
}

export async function commitEditMode(): Promise<void> {
  if (_commitInProgress) return
  _commitInProgress = true
  try {
    const entry = getActiveEditEntry()
    if (!entry) {
      disableEditMode()
      return
    }

    if (getActiveGeomanFeatureId() && getCtx().geoman) {
      const ok = await readGeomanGeometry(entry)
      if (!ok) return
    }

    const saved = await saveGeometry(entry)
    if (!saved) return

    // Update the NARS visual source so the change shows immediately
    // (without requiring a hard refresh to reload from the API)
    updateFeatureGeometry(entry)

    await removeGeomanFeature()
    disableEditMode()

    const phase = PHASES.find((p) => p.key === entry.data.type)
    if (phase) {
      buildDrawControl(phase)
      repatchMarker()
    }
  } finally {
    _commitInProgress = false
  }
}

// ─── CANCEL EDIT MODE ────────────────────────────────────────────────────────

export async function cancelEditMode(): Promise<void> {
  const entry = getActiveEditEntry()
  if (!entry) {
    disableEditMode()
    return
  }

  const snapshot = getActiveEditCoordsSnapshot()
  if (snapshot && snapshot.length > 0) {
    const d = entry.data as { lat?: number; lng?: number; coordinates?: LatLng[] }
    if (entry.type === "marker") {
      // Point features are stored as lat/lng; drag-end overwrote them, so
      // restore the original position (and drop the coordinates array that
      // the generic cancel path used to leave behind on markers).
      d.lat = snapshot[0].lat
      d.lng = snapshot[0].lng
      delete d.coordinates
    } else {
      d.coordinates = snapshot
    }
    updateFeatureGeometry(entry)
  }

  await removeGeomanFeature()
  disableEditMode()
  showToast(t("map_edit_cancelled"), "info")

  const phase = PHASES.find((p) => p.key === entry.data.type)
  if (phase) {
    buildDrawControl(phase)
    repatchMarker()
  }
}
