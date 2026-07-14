// ─── CONTEXT MENU ACTIONS ─────────────────────────────────────────────────────
// enableEditGeometry, editFeatureInfo, removeFeature, and helpers.

import { apiFetch } from "../../api"
import { useAppStore } from "../../stores/appStore"
import { useSelectionStore } from "../../stores/selectionStore"
import { openEditModal } from "../../stores/modalStore"
import { PHASES } from "../../phases"
import { getCtx } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"
import { showToast, showConfirm } from "../../lib/toast"
import type { FeatureTypeKey } from "../../types"
import { getErrorMessage } from "../../lib/errors"
import { recordDelete } from "../undo"
import { enableEditMode } from "../draw/draw-events"
import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import type { LayerEntry } from "../../types"
import { computeCircleRing, closeRing } from "../rendering/geometry"
import { debugError } from "../../utils/debug"
import { updateEndpointMarkers } from "../roads/road-directions"

// ─── LOOKUP ───────────────────────────────────────────────────────────────────

export function findLayerEntryByDbId(dbId: string): LayerEntry | null {
  const layerStore = useLayerStore()
  const state = layerStore.$state
  for (const key of Object.keys(state) as (keyof LayerState)[]) {
    const entries = state[key]
    const entry = entries?.find((e) => e.dbId === dbId)
    if (entry) return entry
  }
  return null
}

// ─── EDIT GEOMETRY ────────────────────────────────────────────────────────────

export function enableEditGeometry(dbId: string): void {
  const selectionStore = useSelectionStore()

  if (selectionStore.selectedFeatureDbId !== null && dbId !== selectionStore.selectedFeatureDbId) {
    showToast("Click the feature to select it first, then right-click to edit.", "info")
    return
  }

  if (selectionStore.selectedFeatureDbId === null) {
    selectionStore.selectFeature(dbId)
  }

  const entry = findLayerEntryByDbId(dbId)
  if (!entry) {
    showToast("Feature not found", "error")
    return
  }

  if (entry.type === "circle") {
    editFeatureInfo(dbId).catch((err) => debugError("[EDIT] editFeatureInfo:", err))
    return
  }

  if (!getCtx().geoman) {
    showToast("Edit mode not available", "error")
    return
  }

  enableEditMode(entry.id)
  showToast("Edit mode: drag vertices to reshape. Right-click to cancel.", "info")
}

// ─── EDIT INFO ────────────────────────────────────────────────────────────────

export async function editFeatureInfo(dbId: string): Promise<void> {
  const entry = findLayerEntryByDbId(dbId)
  if (!entry) {
    showToast("Feature not found", "error")
    return
  }

  if (entry.data.type === "houseEntrances") return

  const phaseIndex = PHASES.findIndex((p) => p.key === entry.data.type)
  if (phaseIndex === -1) {
    showToast("Unknown feature type", "error")
    return
  }

  const result = await openEditModal(phaseIndex, dbId, entry.data)
  if (!result) return

  try {
    await apiFetch(`/api/features/${dbId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ data: { ...entry.data, ...result } }),
    })

    Object.assign(entry.data, result)

    const featuresStore = useFeaturesStore()
    if (entry.type === "circle" && entry.data.radius && entry.data.lat && entry.data.lng) {
      const ring = closeRing(computeCircleRing(entry.data.lat, entry.data.lng, entry.data.radius))
      featuresStore.update(entry.id, {
        geometry: { type: "LineString", coordinates: ring },
        properties: {
          phaseKey: "cityCenter",
          label: entry.data.label,
          geomType: "LineString",
          lineColor: PHASES.find((p) => p.key === "cityCenter")?.color ?? "#e74c3c",
          lineWidth: 6,
        },
      })
    } else {
      featuresStore.update(entry.id, {
        properties: {
          phaseKey: entry.data.type,
          label: result.label,
        },
      })
    }

    showToast("Feature updated.", "success")
  } catch (err) {
    showToast("Save failed: " + getErrorMessage(err), "error")
  }
}

// ─── REMOVE FEATURE ───────────────────────────────────────────────────────────

export async function removeFeature(dbId: string): Promise<void> {
  const entry = findLayerEntryByDbId(dbId)
  if (!entry) {
    showToast("Feature not found", "error")
    return
  }

  const confirmed = await showConfirm(`Delete "${entry.data.label}"?`)
  if (!confirmed) return

  const layerStore = useLayerStore()
  const state = layerStore.$state
  let phaseKey: FeatureTypeKey | "" = ""
  for (const key of Object.keys(state) as (keyof LayerState)[]) {
    const entries = state[key]
    if (entries?.some((f) => f.dbId === dbId)) {
      phaseKey = key
      break
    }
  }

  if (!phaseKey) return
  recordDelete(entry, phaseKey)

  try {
    await apiFetch(`/api/features/${dbId}`, { method: "DELETE" })

    const featuresStore = useFeaturesStore()
    featuresStore.remove(entry.id)
    layerStore.removeFeature(phaseKey, dbId)

    if (phaseKey === "cityCenter") {
      const appStore = useAppStore()
      appStore.cityCenterMode = null
      appStore.cityCenterLatLng = null
    }

    if (phaseKey === "roads") {
      updateEndpointMarkers()
    }

    showToast("Feature deleted.", "success")
  } catch (err) {
    showToast("Delete failed: " + getErrorMessage(err), "error")
  }
}
