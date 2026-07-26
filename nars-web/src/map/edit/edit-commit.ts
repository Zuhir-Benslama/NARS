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
import { debugError } from "../../utils/debug"
import { PHASES } from "../../phases"
import { buildDrawControl } from "../draw/draw-control"
import { repatchMarker } from "../draw/draw-complete"
import type { LayerEntry } from "../../types"
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
        showToast("Road must have at least 2 points.", "error")
        await cancelEditMode()
        return false
      }
    }
    if (geometry.type === "Polygon") {
      const polygonCoords = geometry.coordinates as [number, number][][] | undefined
      if (!polygonCoords?.[0] || polygonCoords[0].length < 3) {
        showToast("Area must have at least 3 points.", "error")
        await cancelEditMode()
        return false
      }
    }

    if (geometry.type === "Point") {
      const c = geometry.coordinates as [number, number]
      entry.data.lat = c[1]
      entry.data.lng = c[0]
    } else if (geometry.type === "Polygon") {
      const coords = (geometry.coordinates as [number, number][][])[0]
      entry.data.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))
      if (entry.type === "circle" && coords.length >= 3) {
        const { lat, lng } = computeCircleCenter(coords)
        entry.data.lat = lat
        entry.data.lng = lng
        entry.data.radius = computeCircleRadius(entry.data.lat, entry.data.lng, coords)
      }
    } else {
      // LineString
      const coords = geometry.coordinates as [number, number][]
      entry.data.coordinates = coords.map((c) => ({ lat: c[1], lng: c[0] }))
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
    showToast("Geometry saved.", "success")
    return true
  } catch (err) {
    debugError("Failed to save geometry:", err)
    showToast("Failed to save geometry changes", "error")
    return false
  }
}

// ─── COMMIT EDIT MODE ────────────────────────────────────────────────────────

function updateFeatureGeometry(entry: LayerEntry): void {
  const featuresStore = useFeaturesStore()
  if (entry.data.lat != null && entry.data.lng != null) {
    if (entry.type === "circle" && entry.data.radius) {
      const ring = closeRing(computeCircleRing(entry.data.lat, entry.data.lng, entry.data.radius))
      featuresStore.update(entry.id, {
        geometry: { type: "LineString", coordinates: ring },
      })
    } else {
      const geom: GeoJSON.Point = {
        type: "Point",
        coordinates: [entry.data.lng, entry.data.lat],
      }
      featuresStore.update(entry.id, { geometry: geom })
    }
  } else if (entry.data.coordinates && entry.data.coordinates.length > 0) {
    const coords = entry.data.coordinates.map((c) => [c.lng, c.lat])
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
  if (snapshot) {
    entry.data.coordinates = snapshot
    updateFeatureGeometry(entry)
  }

  await removeGeomanFeature()
  disableEditMode()
  showToast("Edit cancelled.", "info")

  const phase = PHASES.find((p) => p.key === entry.data.type)
  if (phase) {
    buildDrawControl(phase)
    repatchMarker()
  }
}
