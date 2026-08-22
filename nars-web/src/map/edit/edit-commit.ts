// ─── EDIT COMMIT ──────────────────────────────────────────────────────────────
// Handles saving edited geometry to API, canceling edits with restore,
// and cleaning up Geoman state.

import { apiFetch } from "../../api"
import { getCtx } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"
import { useLayerStore } from "../../stores/layerStore"
import { computeCircleRadius, computeCircleCenter } from "../rendering/geometry"
import { showToast } from "../../lib/toast"
import { t } from "../../i18n"
import { debugError } from "../../utils/debug"
import { PHASES } from "../../phases"
import { buildDrawControl } from "../draw/draw-control"
import { repatchMarker } from "../draw/draw-complete"
import { featureDataToGeometry } from "../features/feature-data"
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

    const patch: { lat?: number; lng?: number; radius?: number; coordinates?: LatLng[] } = {}

    if (geometry.type === "Point") {
      const c = geometry.coordinates as [number, number]
      patch.lat = c[1]
      patch.lng = c[0]
    } else if (geometry.type === "Polygon") {
      const coords = (geometry.coordinates as [number, number][][])[0]
      patch.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))
      if (entry.type === "circle" && coords.length >= 3) {
        const { lat, lng } = computeCircleCenter(coords)
        patch.lat = lat
        patch.lng = lng
        patch.radius = computeCircleRadius(lat, lng, coords)
      }
    } else {
      // LineString
      const coords = geometry.coordinates as [number, number][]
      patch.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))
    }

    useLayerStore().updateFeature(entry.data.type, entry.dbId, patch)
  } catch (err) {
    debugError("Failed to read Geoman geometry:", err)
    // Without the fresh geometry the commit below would silently PUT the
    // STALE pre-edit data and report success. Abort instead and say why.
    showToast(t("map_geometry_save_failed"), "error")
    return false
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
  // Shared mapper — this used to be a fourth divergent copy of the
  // FeatureData→GeoJSON mapping (see feature-data.ts).
  featuresStore.update(entry.id, {
    geometry: featureDataToGeometry(
      { lat: d.lat, lng: d.lng, radius: d.radius, coordinates: d.coordinates },
      entry.type,
    ),
  })
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
    if (entry.type === "marker") {
      // Point features are stored as lat/lng; drag-end overwrote them, so
      // restore the original position and drop the coordinates array that
      // the generic cancel path leaves behind on markers.
      useLayerStore().updateFeature(entry.data.type, entry.dbId, {
        lat: snapshot[0].lat,
        lng: snapshot[0].lng,
      })
      // Key REMOVAL, not an undefined patch: updateFeature merges via
      // Object.assign, which would keep the key present with an undefined
      // value. If updateFeature ever gains delete semantics, fold this in.
      delete entry.data.coordinates
    } else {
      useLayerStore().updateFeature(entry.data.type, entry.dbId, { coordinates: snapshot })
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
