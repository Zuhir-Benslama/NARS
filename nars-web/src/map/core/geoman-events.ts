// ─── GEOMAN EVENTS ────────────────────────────────────────────────────────────
// Event handlers for Geoman's internal events: vertex drag tracking,
// gm:editend (live geometry update), gm:remove (feature deletion),
// and double-click vertex removal in edit mode.

import { apiFetch } from "../../api"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import { ctx, featuresStore } from "./state"
import { getActiveSnapPhases, snapPointForEdit, setEditDragActive } from "../snapping/snapping"
import { disableEditMode, getActiveEditEntry, isEditMode } from "../edit/edit-mode"
import { recordDelete } from "../undo"
import { refreshLayerVisibility } from "../rendering/labels"
import { showToast } from "../../lib/toast"
import { debugError } from "../../utils/debug"
import type { LayerEntry } from "../../types"
import type {
  GeomanEditEvent,
  GeomanRemoveEvent,
  GeomanMarkerDragEvent,
  ActionInstances,
  GeomanFeatures,
  GeomanFeatureStoreEntry,
} from "./geoman-types"
import type { MapMouseEvent as MapLibreMapMouseEvent } from "maplibre-gl"

// ─── REGISTRATION ─────────────────────────────────────────────────────────────

export function registerGeomanEvents(): void {
  const map = ctx.map
  const geoman = ctx.geoman
  if (!geoman) {
    debugError("Geoman not initialized")
    return
  }

  // ── Track vertex drag state for snap pause during edit ────────────────────
  // Matching the reference: editDragActive tells onSnapMove to NOT disable
  // snap while a vertex is being dragged.
  let _draggedVertexIndex: number | null = null
  map.on("pm:markerdragstart", (e: GeomanMarkerDragEvent) => {
    setEditDragActive(true)
    // Capture the dragged vertex index from Geoman's event for O(1) snap
    _draggedVertexIndex = e?.markerIndex ?? e?.vertexIndex ?? null
  })
  map.on("pm:markerdragend", () => {
    setEditDragActive(false)
    _draggedVertexIndex = null
  })

  // ── Double-click to remove a vertex in edit mode ──────────────────────────
  map.on("dblclick", (e: MapLibreMapMouseEvent) => {
    if (!isEditMode()) return
    e.preventDefault()

    const gm = ctx.geoman
    if (!gm) return

    const actionInstances = gm.actionInstances as ActionInstances | undefined
    const shapeHelper = actionInstances?.["helper__shape_markers"]
    if (!shapeHelper) return

    // Query ALL features — vertex markers are on Geoman's internal sources
    const allFeatures = map.queryRenderedFeatures(e.point)

    // Find Point features (vertex markers) — filter out NARS 'features' source
    const vertexFeatures = allFeatures.filter(
      (f) => f.source !== "features" && f.geometry?.type === "Point",
    )

    if (vertexFeatures.length === 0) return

    // Match the hit coordinate to a MarkerData in the feature store
    const hitCoord = (vertexFeatures[0].geometry as GeoJSON.Point).coordinates
    const [hitLng, hitLat] = hitCoord

    const featureStore = (gm.features as GeomanFeatures | undefined)?.featureStore as
      | Map<string, GeomanFeatureStoreEntry>
      | undefined
    if (!featureStore) return
    for (const [, featureData] of featureStore) {
      if (!featureData?.markers) continue
      for (const [, markerData] of featureData.markers) {
        if (markerData?.type !== "vertex") continue
        const mc = markerData.position?.coordinate
        if (mc && Math.abs(mc[0] - hitLng) < 0.00001 && Math.abs(mc[1] - hitLat) < 0.00001) {
          shapeHelper.sendMarkerRightClickEvent(featureData, markerData)
          return
        }
      }
    }
  })

  // gm:editend fires after EVERY single vertex drag — not when the user is
  // "done editing". We update the NARS render layer live so the user sees
  // the shape change, but we stay in edit mode so they can keep dragging.
  // Saving to the API and exiting edit mode happens only via commitEditMode().
  //
  // Snap integration: after each vertex drag, find the closest vertex in the
  // ring and snap it in-place if within threshold. Matching the reference's
  // hookMarker dragend handler.
  map.on("gm:editend", (e: GeomanEditEvent) => {
    const feature = e.feature
    if (!feature) return

    const layerEntry = getActiveEditEntry()
    if (!layerEntry) return

    const geometry = feature._geoJson?.geometry
    if (!geometry) return

    // Mirror updated geometry into NARS render source immediately
    if (geometry.type === "Point") {
      const c = geometry.coordinates as [number, number]
      layerEntry.data.lat = c[1]
      layerEntry.data.lng = c[0]
    } else if (
      geometry.type === "LineString" ||
      geometry.type === "MultiLineString" ||
      geometry.type === "MultiPoint"
    ) {
      const coords = geometry.coordinates as [number, number][]
      const newCoords = coords.map((c) => ({ lat: c[1], lng: c[0] }))

      // Per-vertex snap: after dragging, snap the dragged vertex if near
      // a target. Use the captured vertex index from pm:markerdragstart
      // for O(1) lookup instead of scanning all vertices.
      const activePhases = getActiveSnapPhases()
      if (activePhases.length > 0) {
        // If we have the dragged vertex index, snap only that one.
        // Otherwise fall back to scanning all vertices.
        if (_draggedVertexIndex !== null && _draggedVertexIndex < newCoords.length) {
          const coord = newCoords[_draggedVertexIndex]
          const px = ctx.map.project([coord.lng, coord.lat])
          const snapped = snapPointForEdit(px.x, px.y, layerEntry.id)
          if (snapped) {
            newCoords[_draggedVertexIndex].lat = snapped.lat
            newCoords[_draggedVertexIndex].lng = snapped.lng
          }
        } else {
          // Fallback: scan all vertices (should rarely happen)
          for (let i = 0; i < newCoords.length; i++) {
            const coord = newCoords[i]
            const px = ctx.map.project([coord.lng, coord.lat])
            const snapped = snapPointForEdit(px.x, px.y, layerEntry.id)
            if (snapped) {
              newCoords[i].lat = snapped.lat
              newCoords[i].lng = snapped.lng
            }
          }
        }
      }

      layerEntry.data.coordinates = newCoords
      featuresStore.update(layerEntry.id, { geometry })
    } else if (geometry.type === "MultiPolygon") {
      const coords = geometry.coordinates[0][0] as [number, number][]
      const newCoords = coords.map((c) => ({ lat: c[1], lng: c[0] }))
      layerEntry.data.coordinates = newCoords
      featuresStore.update(layerEntry.id, { geometry })
    }
  })

  map.on("gm:remove", async (e: GeomanRemoveEvent) => {
    const feature = e.feature
    if (!feature) return

    const dbId: string | undefined = feature._geoJson?.properties?.dbId
    if (!dbId) {
      showToast("Cannot delete: feature ID not found", "error")
      return
    }

    // If we're currently editing this feature, exit edit mode first to
    // prevent stale state (commitEditMode would try to update a deleted feature).
    const activeEntry = getActiveEditEntry()
    if (activeEntry?.dbId === dbId) {
      disableEditMode()
    }

    // Find and record the entry BEFORE deleting
    let removed: LayerEntry | null = null
    let phaseKey = ""
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    for (const key of Object.keys(state)) {
      const entries = state[key as keyof LayerState]
      const entry = entries?.find((f) => f.dbId === dbId)
      if (entry) {
        removed = entry
        phaseKey = key
        break
      }
    }
    if (removed) recordDelete(removed, phaseKey)

    try {
      const response = await apiFetch(`/api/delete/${dbId}`, {
        method: "DELETE",
      })
      if (!response.ok) {
        showToast(`Delete failed: HTTP ${response.status}`, "error")
        return
      }

      if (removed) {
        featuresStore.remove(removed.id)
        const entries = state[phaseKey as keyof LayerState]
        if (entries) {
          const filtered = entries.filter((f) => f.dbId !== dbId)
          ;(state as unknown as Record<string, LayerEntry[]>)[phaseKey] = filtered
        }
        useAppStore().syncCounts()
        refreshLayerVisibility()
      }

      showToast("Feature deleted.", "success")
    } catch (err) {
      showToast("Delete failed: " + (err as Error).message, "error")
    }
  })
}
