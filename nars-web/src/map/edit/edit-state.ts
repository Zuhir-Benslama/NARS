// ─── EDIT STATE ───────────────────────────────────────────────────────
// Shared state for edit mode: active entry, coordinate snapshot, and
// Geoman feature tracking. Also contains lookup helpers and fill suppression.

import { setSelectedFeature } from "../../stores"
import { useLayerStore } from "../../stores/layerStore"
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
import { hideEditSaveButton } from "./edit-ui"

export let isEditMode = false

let activeGeomanFeatureId: string | null = null
let activeEditEntry: LayerEntry | null = null
let activeEditCoordsSnapshot: LatLng[] | null = null

export function getActiveEditEntry(): LayerEntry | null {
  return activeEditEntry
}

export function getActiveGeomanFeatureId(): string | null {
  return activeGeomanFeatureId
}

export function setActiveGeomanFeatureId(id: string | null): void {
  activeGeomanFeatureId = id
}

export function getActiveEditCoordsSnapshot(): LatLng[] | null {
  return activeEditCoordsSnapshot
}

export function setActiveEditCoordsSnapshot(snapshot: LatLng[] | null): void {
  activeEditCoordsSnapshot = snapshot
}

export function setActiveEditEntry(entry: LayerEntry | null): void {
  activeEditEntry = entry
}

// ─── STATE RESET (for testing & HMR) ──────────────────────────────────────────

export function resetEditState(): void {
  isEditMode = false
  activeGeomanFeatureId = null
  activeEditEntry = null
  activeEditCoordsSnapshot = null
}

// ─── DISABLE EDIT MODE ───────────────────────────────────────────────────

export function disableEditMode(): void {
  if (!ctx.geoman) return
  unpatchMarkerPointerSnap()
  ctx.geoman.disableGlobalEditMode()
  isEditMode = false
  setEditModeActive(false)
  activeGeomanFeatureId = null
  activeEditEntry = null
  activeEditCoordsSnapshot = null
  setSnapExclude(null)
  setSelectedFeature(null)
  updateSelectionHighlight(null)
  enableCrosshair()
  reEnableSnapping()
  hideEditSaveButton()
}

function reEnableSnapping(): void {
  disableSnapping()
  enableSnapping()
}

// ─── LOOKUP HELPERS ───────────────────────────────────────────────────

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

// ─── GEOMAN FILL SUPPRESSION ──────────────────────────────────────────

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
