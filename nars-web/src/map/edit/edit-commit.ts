// ─── EDIT COMMIT ──────────────────────────────────────────────────────────────
// Handles saving edited geometry to API, canceling edits with restore,
// and cleaning up Geoman state.

import { apiFetch } from "../../api"
import { featuresStore, ctx } from "../core/state"
import { computeCircleRing, computeCircleRadius, closeRing } from "../rendering/geometry"
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

// ─── GEOMAN FEATURE REMOVAL ──────────────────────────────────────────────────

async function removeGeomanFeature(): Promise<void> {
  const geomanId = getActiveGeomanFeatureId()
  if (!ctx.geoman || !geomanId) return
  try {
    await ctx.geoman.features.delete(geomanId)
  } catch {
    // Feature may already be gone
  }
  setActiveGeomanFeatureId(null)
}

// ─── GEOMETRY EXTRACTION ─────────────────────────────────────────

async function readGeomanGeometry(entry: LayerEntry): Promise<boolean> {
  if (!getActiveGeomanFeatureId() || !ctx.geoman) return true
  try {
    const geomanFeatures = await ctx.geoman.features.getAll()
    const geomanFeature = (
      geomanFeatures as {
        features?: Array<{ id?: string; _geoJson?: { geometry?: unknown } }>
      }
    ).features?.find((f) => f.id === getActiveGeomanFeatureId())
    const rawGeometry = geomanFeature?._geoJson?.geometry
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
        let sumLat = 0,
          sumLng = 0
        for (const [lng, lat] of coords) {
          sumLat += lat
          sumLng += lng
        }
        entry.data.lat = sumLat / coords.length
        entry.data.lng = sumLng / coords.length
        entry.data.radius = computeCircleRadius(entry.data.lat, entry.data.lng, coords)
      }
    } else {
      const coords =
        geometry.type === "Polygon"
          ? (geometry.coordinates as [number, number][][])[0]
          : (geometry.coordinates as [number, number][])
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

export async function commitEditMode(): Promise<void> {
  const entry = getActiveEditEntry()
  if (!entry) {
    disableEditMode()
    return
  }

  if (getActiveGeomanFeatureId() && ctx.geoman) {
    const ok = await readGeomanGeometry(entry)
    if (!ok) return
  }

  await saveGeometry(entry)

  if (
    entry.type === "circle" &&
    entry.data.lat != null &&
    entry.data.lng != null &&
    entry.data.radius
  ) {
    const ring = closeRing(computeCircleRing(entry.data.lat, entry.data.lng, entry.data.radius))
    featuresStore.update(entry.id, {
      geometry: { type: "LineString", coordinates: ring },
    })
  }

  await removeGeomanFeature()
  disableEditMode()

  const phase = PHASES.find((p) => p.key === entry.data.type)
  if (phase) {
    buildDrawControl(phase)
    repatchMarker()
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
    if (entry.data.lat != null && entry.data.lng != null) {
      if (entry.type === "circle" && entry.data.radius) {
        const ring = computeCircleRing(entry.data.lat, entry.data.lng, entry.data.radius)
        featuresStore.update(entry.id, {
          geometry: { type: "LineString", coordinates: closeRing(ring) },
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
        const geom: GeoJSON.LineString = {
          type: "LineString",
          coordinates: coords,
        }
        featuresStore.update(entry.id, { geometry: geom })
      } else if (entry.type === "circle") {
        const geom: GeoJSON.LineString = {
          type: "LineString",
          coordinates: closeRing(coords as [number, number][]),
        }
        featuresStore.update(entry.id, { geometry: geom })
      } else {
        const geom: GeoJSON.Polygon = {
          type: "Polygon",
          coordinates: [closeRing(coords as [number, number][])],
        }
        featuresStore.update(entry.id, { geometry: geom })
      }
    }
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
