// ─── GEOMAN EVENTS ────────────────────────────────────────────────────────────
// Event handlers for Geoman's internal events: vertex drag tracking,
// gm:editend (live geometry update), gm:remove (feature deletion),
// and double-click vertex removal in edit mode.

import { apiFetch } from "../../api"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore, LAYER_KEYS } from "../../stores/layerStore"
import { useEditStore } from "../../stores/editStore"
import type { LayerState } from "../../stores/layerStore"
import { getCtx } from "./state"
import { useFeaturesStore } from "../../stores/featuresStore"
import { getActiveSnapPhases, snapPointForEdit, setEditDragActive } from "../snapping/snapping"
import { disableEditMode, getActiveEditEntry, isEditMode } from "../edit/edit-mode"
import { recordDelete } from "../undo"
import { refreshLayerVisibility } from "../rendering/labels"
import { getErrorMessage } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import { debugError } from "../../utils/debug"
import type { FeatureTypeKey, LayerEntry } from "../../types"
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

function onVertexDragStart(e: GeomanMarkerDragEvent): void {
  setEditDragActive(true)
  useEditStore().draggedVertexIndex = e?.markerIndex ?? e?.vertexIndex ?? null
}

function onVertexDragEnd(): void {
  setEditDragActive(false)
  useEditStore().draggedVertexIndex = null
}

function onDblClick(e: MapLibreMapMouseEvent): void {
  if (!isEditMode()) return
  e.preventDefault()

  const { geoman: gm, map } = getCtx()
  if (!gm) return

  const actionInstances = gm.actionInstances as ActionInstances | undefined
  const shapeHelper = actionInstances?.["helper__shape_markers"]
  if (!shapeHelper) return

  const allFeatures = map.queryRenderedFeatures(e.point)

  const vertexFeatures = allFeatures.filter(
    (f) => f.source !== "features" && f.geometry?.type === "Point",
  )

  if (vertexFeatures.length === 0) return

  const hitCoord = (vertexFeatures[0].geometry as GeoJSON.Point).coordinates
  const [hitLng, hitLat] = hitCoord

  const featureStore = (gm.features as GeomanFeatures | undefined)?.featureStore as
    Map<string, GeomanFeatureStoreEntry> | undefined
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
}

function onEditEnd(e: GeomanEditEvent): void {
  const { map } = getCtx()
  const feature = e.feature
  if (!feature) return

  const layerEntry = getActiveEditEntry()
  if (!layerEntry) return

  const geometry = feature._geoJson?.geometry
  if (!geometry) return

  const featuresStore = useFeaturesStore()

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

    const activePhases = getActiveSnapPhases()
    if (activePhases.length > 0) {
      const editStore = useEditStore()
      if (
        editStore.draggedVertexIndex !== null &&
        editStore.draggedVertexIndex < newCoords.length
      ) {
        const coord = newCoords[editStore.draggedVertexIndex]
        const px = map.project([coord.lng, coord.lat])
        const snapped = snapPointForEdit(px.x, px.y, layerEntry.id)
        if (snapped) {
          newCoords[editStore.draggedVertexIndex].lat = snapped.lat
          newCoords[editStore.draggedVertexIndex].lng = snapped.lng
        }
      } else {
        for (let i = 0; i < newCoords.length; i++) {
          const coord = newCoords[i]
          const px = map.project([coord.lng, coord.lat])
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
}

async function onRemove(e: GeomanRemoveEvent): Promise<void> {
  const feature = e.feature
  if (!feature) return

  const dbId: string | undefined = feature._geoJson?.properties?.dbId
  if (!dbId) {
    showToast("Cannot delete: feature ID not found", "error")
    return
  }

  const activeEntry = getActiveEditEntry()
  if (activeEntry?.dbId === dbId) {
    disableEditMode()
  }

  let removed: LayerEntry | null = null
  let phaseKey: FeatureTypeKey | "" = ""
  const layerStore = useLayerStore()
  const state = layerStore.$state
  for (const key of LAYER_KEYS) {
    const entries = state[key as keyof LayerState]
    const entry = entries?.find((f) => f.dbId === dbId)
    if (entry) {
      removed = entry
      phaseKey = key as keyof LayerState
      break
    }
  }
  if (removed && phaseKey) recordDelete(removed, phaseKey)
  if (!removed || !phaseKey) return

  try {
    const response = await apiFetch(`/api/features/${dbId}`, {
      method: "DELETE",
    })
    if (!response.ok) {
      showToast(`Delete failed: HTTP ${response.status}`, "error")
      return
    }

    useFeaturesStore().remove(removed.id)
    layerStore.removeFeature(phaseKey, dbId)
    useAppStore().syncCounts()
    refreshLayerVisibility()

    showToast("Feature deleted.", "success")
  } catch (err) {
    showToast("Delete failed: " + getErrorMessage(err), "error")
  }
}

export function registerGeomanEvents(): void {
  const { map, geoman } = getCtx()
  if (!geoman) {
    debugError("Geoman not initialized")
    return
  }

  map.on("pm:markerdragstart", onVertexDragStart)
  map.on("pm:markerdragend", onVertexDragEnd)
  map.on("dblclick", onDblClick)
  map.on("gm:editend", onEditEnd)
  map.on("gm:remove", onRemove)
}
