import { useSelectionStore } from "../../stores/selectionStore"
import { useLayerStore } from "../../stores/layerStore"
import { useEditStore } from "../../stores/editStore"
import type { LayerState } from "../../stores/layerStore"
import type { LayerEntry, LatLng } from "../../types"
import { ctx, updateSelectionHighlight } from "../core/state"
import {
  enableCrosshair,
  disableSnapping,
  enableSnapping,
  setSnapExclude,
  setEditModeActive,
} from "../snapping/snapping"
import { unpatchMarkerPointerSnap } from "./edit-snap"

export function isEditMode(): boolean {
  return useEditStore().isEditMode
}

export function getActiveGeomanFeatureId(): string | null {
  return useEditStore().activeGeomanFeatureId
}

export function getActiveEditEntry(): LayerEntry | null {
  return useEditStore().activeEditEntry
}

export function getActiveEditCoordsSnapshot(): LatLng[] | null {
  return useEditStore().activeEditCoordsSnapshot
}

export function setActiveGeomanFeatureId(id: string | null): void {
  useEditStore().setActiveGeomanFeatureId(id)
}

export function setActiveEditCoordsSnapshot(snapshot: LatLng[] | null): void {
  useEditStore().setActiveEditCoordsSnapshot(snapshot)
}

export function setActiveEditEntry(entry: LayerEntry | null): void {
  useEditStore().setActiveEditEntry(entry)
}

export function resetEditState(): void {
  useEditStore().resetEdit()
}

export function disableEditMode(): void {
  if (!ctx.geoman) return
  const store = useEditStore()
  unpatchMarkerPointerSnap()
  ctx.geoman.disableGlobalEditMode()
  store.isEditMode = false
  setEditModeActive(false)
  store.setActiveGeomanFeatureId(null)
  store.setActiveEditEntry(null)
  store.setActiveEditCoordsSnapshot(null)
  setSnapExclude(null)
  useSelectionStore().selectFeature(null)
  updateSelectionHighlight(null)
  enableCrosshair()
  reEnableSnapping()
}

function reEnableSnapping(): void {
  disableSnapping()
  enableSnapping()
}

export function findLayerEntryByFeatureId(featureId: string | undefined): LayerEntry | null {
  if (!featureId) return null
  const layerStore = useLayerStore()
  const state = layerStore.$state as LayerState
  for (const key of Object.keys(state)) {
    const entries = state[key as keyof LayerState]
    const entry = entries?.find((e) => e.id === featureId)
    if (entry) return entry
  }
  return null
}

export function suppressGeomanFill(): void {
  for (const layerId of ["gm_main-polygon__fill-layer-0", "gm_temporary-polygon__fill-layer-0"]) {
    try {
      if (ctx.map.getLayer(layerId)) {
        ctx.map.setPaintProperty(layerId, "fill-opacity", 0)
      }
    } catch {
      /* layer may not exist */
    }
  }

  for (const layerId of ["gm_main-circle__circle-layer-0", "gm_temporary-circle__circle-layer-0"]) {
    try {
      if (ctx.map.getLayer(layerId)) {
        ctx.map.setPaintProperty(layerId, "circle-opacity", 0)
        ctx.map.setPaintProperty(layerId, "circle-stroke-opacity", 0)
      }
    } catch {
      /* layer may not exist */
    }
  }
}

export function ensureGeomanDrawEdgesVisible(): void {
  for (const layerId of [
    "gm_temporary-polygon__line-layer-0",
    "gm_temporary-line__line-layer-0",
  ]) {
    try {
      if (ctx.map.getLayer(layerId)) {
        ctx.map.setPaintProperty(layerId, "line-opacity", 0.8)
        ctx.map.setPaintProperty(layerId, "line-color", "#3498db")
        ctx.map.setPaintProperty(layerId, "line-width", 3)
      }
    } catch {
      /* layer may not exist yet */
    }
  }

  for (const layerId of [
    "gm_main-polygon__line-layer-0",
    "gm_main-line__line-layer-0",
  ]) {
    try {
      if (ctx.map.getLayer(layerId)) {
        ctx.map.setPaintProperty(layerId, "line-opacity", 0.8)
        ctx.map.setPaintProperty(layerId, "line-color", "#3498db")
        ctx.map.setPaintProperty(layerId, "line-width", 3)
      }
    } catch {
      /* layer may not exist yet */
    }
  }

  // Add a NARS fallback edge layer that reads from gm_temporary source
  // to ensure edges are visible during draw regardless of Geoman's own layers.
  if (!ctx.map.getLayer("nars-temp-edge") && !!ctx.map.getSource("gm_temporary")) {
    try {
      ctx.map.addLayer({
        id: "nars-temp-edge",
        type: "line",
        source: "gm_temporary",
        filter: ["in", ["get", "__gm_shape"], ["literal", ["line", "polygon"]]],
        paint: {
          "line-color": "#3498db",
          "line-width": 3,
          "line-opacity": 0.8,
        },
      })
    } catch {
      /* nars-temp-edge may already exist */
    }
  }
}
