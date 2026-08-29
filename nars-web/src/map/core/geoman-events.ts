// ─── GEOMAN EVENTS ────────────────────────────────────────────────────────────
// Event handlers for Geoman's internal events: vertex drag tracking,
// gm:editend (live geometry update), gm:remove (feature deletion),
// and double-click vertex removal in edit mode.

import { apiFetch } from "../../api"
import { useLayerStore, LAYER_KEYS } from "../../stores/layerStore"
import { useEditStore } from "../../stores/editStore"
import type { LayerState } from "../../stores/layerStore"
import { getCtx, type MaplibreFeature } from "./state"
import { useFeaturesStore } from "../../stores/featuresStore"
import { getActiveSnapPhases, snapPointForEdit, setEditDragActive } from "../snapping/snapping"
import { disableEditMode, getActiveEditEntry, isEditMode } from "../edit/edit-mode"
import { recordDelete } from "../undo"
import { refreshLayerVisibility } from "../rendering/labels"
import { getUserMessageKey } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import { t } from "../../i18n"
import { PHASES } from "../../phases"
import { buildGeoJsonFeature } from "../features/loader-build"
import type { FeatureTypeKey, LayerEntry, LatLng } from "../../types"
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
  useEditStore().setDraggedVertexIndex(e?.markerIndex ?? e?.vertexIndex ?? null)
}

function onVertexDragEnd(): void {
  setEditDragActive(false)
  useEditStore().setDraggedVertexIndex(null)
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
  const layerStore = useLayerStore()

  if (geometry.type === "Point") {
    const c = geometry.coordinates
    layerStore.updateFeatureData(layerEntry.dbId, { lat: c[1], lng: c[0] })
  } else if (
    geometry.type === "LineString" ||
    geometry.type === "MultiLineString" ||
    geometry.type === "MultiPoint"
  ) {
    const newCoords = linearGeometryToLatLng(geometry)
    if (newCoords.length === 0) return

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

    layerStore.updateFeatureData(layerEntry.dbId, { coordinates: newCoords })
    featuresStore.update(layerEntry.id, { geometry })
  } else if (geometry.type === "MultiPolygon") {
    const outerRing = geometry.coordinates[0]?.[0]
    if (!outerRing || outerRing.length === 0) return
    const newCoords = positionsToLatLng(outerRing)
    layerStore.updateFeatureData(layerEntry.dbId, { coordinates: newCoords })
    featuresStore.update(layerEntry.id, { geometry })
  }
}

/**
 * Convert [lng, lat] positions to the {lat, lng} shape the layer store expects.
 */
function positionsToLatLng(coords: GeoJSON.Position[]): LatLng[] {
  return coords.map((c) => ({ lat: c[1], lng: c[0] }))
}

/**
 * Flatten the editable linear geometries into a single {lat, lng} list.
 * MultiLineString is nested arrays, so each line is flattened in order.
 */
function linearGeometryToLatLng(
  geometry: GeoJSON.LineString | GeoJSON.MultiLineString | GeoJSON.MultiPoint,
): LatLng[] {
  if (geometry.type === "MultiLineString") {
    return geometry.coordinates.flatMap((line) => positionsToLatLng(line))
  }
  return positionsToLatLng(geometry.coordinates)
}

async function onRemove(e: GeomanRemoveEvent): Promise<void> {
  const feature = e.feature
  if (!feature) return

  const dbId: string | undefined = feature._geoJson?.properties?.dbId
  if (!dbId) {
    showToast(t("map_cannot_delete_no_id"), "error")
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
  if (!removed || !phaseKey) return

  try {
    const response = await apiFetch(`/api/features/${dbId}`, {
      method: "DELETE",
    })
    if (!response.ok) {
      showToast(t("map_delete_http_failed", { status: response.status }), "error")
      restoreRemovedFeature(removed, phaseKey)
      return
    }

    recordDelete(removed, phaseKey)

    useFeaturesStore().remove(removed.id)
    layerStore.removeFeature(phaseKey, dbId)
    refreshLayerVisibility()

    showToast(t("map_feature_deleted"), "success")
  } catch (err) {
    showToast(t("map_delete_failed", { error: t(getUserMessageKey(err)) }), "error")
    restoreRemovedFeature(removed, phaseKey)
  }
}

// Re-render a feature whose DELETE failed, so the map does not stay empty.
// No-op when the feature is still present in the store (the failed request
// never removed it).
function restoreRemovedFeature(removed: LayerEntry, phaseKey: FeatureTypeKey): void {
  const featuresStore = useFeaturesStore()
  if (featuresStore.getAll().some((f) => f.id === removed.id)) return

  const phase = PHASES.find((p) => p.key === phaseKey)
  if (!phase) return
  const geojson = buildGeoJsonFeature(removed.dbId, removed.data, phase)
  if (!geojson) return

  featuresStore.add({
    id: removed.id,
    geometry: geojson.geometry,
    properties: geojson.properties as MaplibreFeature["properties"],
  })
}

export function registerGeomanEvents(): void {
  // Geoman is initialized lazily on first edit/draw, but geoman dispatches
  // its events through the map object, so these listeners are registered up
  // front (independent of whether the geoman bundle is loaded yet). Each
  // handler guards on getCtx().geoman internally.
  const { map } = getCtx()
  map.on("pm:markerdragstart", onVertexDragStart)
  map.on("pm:markerdragend", onVertexDragEnd)
  map.on("dblclick", onDblClick)
  map.on("gm:editend", onEditEnd)
  map.on("gm:remove", onRemove)
}

export function unregisterGeomanEvents(): void {
  const { map } = getCtx()
  map.off("pm:markerdragstart", onVertexDragStart)
  map.off("pm:markerdragend", onVertexDragEnd)
  map.off("dblclick", onDblClick)
  map.off("gm:editend", onEditEnd)
  map.off("gm:remove", onRemove)
}
