// ─── CONTEXT MENU ACTIONS ─────────────────────────────────────────────────────
// enableEditGeometry, editFeatureInfo, removeFeature, and helpers.

import { apiFetch } from "../../api"
import { useSelectionStore } from "../../stores/selectionStore"
import { openEditModal } from "../../stores/modalStore"
import { PHASES, CITY_CENTER_COLOR } from "../../phases"
import { CITY_CENTER_CONFIG } from "../../config"
import { getCtx } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"
import { useLayerStore, LAYER_KEYS } from "../../stores/layerStore"
import { showToast, showConfirm } from "../../lib/toast"
import type { FeatureTypeKey } from "../../types"
import { getUserMessageKey } from "../../lib/errors"
import { t } from "../../i18n"
import { recordDelete } from "../undo"
import { enableEditMode } from "../draw/draw-events"
import type { LayerEntry } from "../../types"
import { featureDataToGeometry } from "../features/feature-data"
import { debugError } from "../../utils/debug"
import { updateEndpointMarkers } from "../roads/road-directions"

// ─── LOOKUP ───────────────────────────────────────────────────────────────────

export function findLayerEntryByDbId(dbId: string): LayerEntry | null {
  return useLayerStore().getFeature(dbId)
}

// ─── EDIT GEOMETRY ────────────────────────────────────────────────────────────

export function enableEditGeometry(dbId: string): void {
  const selectionStore = useSelectionStore()

  if (selectionStore.selectedFeatureDbId !== null && dbId !== selectionStore.selectedFeatureDbId) {
    showToast(t("map_select_feature_first"), "info")
    return
  }

  if (selectionStore.selectedFeatureDbId === null) {
    selectionStore.setSelectedFeatureDbId(dbId)
  }

  const entry = findLayerEntryByDbId(dbId)
  if (!entry) {
    showToast(t("map_feature_not_found"), "error")
    return
  }

  if (entry.type === "circle") {
    editFeatureInfo(dbId).catch((err) => debugError("[EDIT] editFeatureInfo:", err))
    return
  }

  if (!getCtx().geoman) {
    showToast(t("map_edit_mode_unavailable"), "error")
    return
  }

  enableEditMode(entry.id)
  showToast(t("map_edit_mode_hint"), "info")
}

// ─── EDIT INFO ────────────────────────────────────────────────────────────────

export async function editFeatureInfo(dbId: string): Promise<void> {
  const entry = findLayerEntryByDbId(dbId)
  if (!entry) {
    showToast(t("map_feature_not_found"), "error")
    return
  }

  if (entry.data.type === "houseEntrances") return

  const phaseIndex = PHASES.findIndex((p) => p.key === entry.data.type)
  if (phaseIndex === -1) {
    showToast(t("map_unknown_feature_type"), "error")
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

    useLayerStore().updateFeature(entry.data.type, dbId, result)

    const featuresStore = useFeaturesStore()
    const d = entry.data as { radius?: number; lat?: number; lng?: number; label: string }
    if (entry.type === "circle" && d.radius && d.lat && d.lng) {
      featuresStore.update(entry.id, {
        geometry: featureDataToGeometry(d, "circle"),
        properties: {
          phaseKey: "cityCenter",
          label: entry.data.label,
          geomType: "LineString",
          lineColor: CITY_CENTER_COLOR,
          lineWidth: CITY_CENTER_CONFIG.ringStrokeWidth,
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

    showToast(t("map_feature_updated"), "success")
  } catch (err) {
    showToast(t("map_save_failed", { error: t(getUserMessageKey(err)) }), "error")
  }
}

// ─── REMOVE FEATURE ───────────────────────────────────────────────────────────

export async function removeFeature(dbId: string): Promise<void> {
  const entry = findLayerEntryByDbId(dbId)
  if (!entry) {
    showToast(t("map_feature_not_found"), "error")
    return
  }

  const confirmed = await showConfirm(t("map_delete_confirm", { label: entry.data.label }))
  if (!confirmed) return

  const layerStore = useLayerStore()
  const state = layerStore.$state
  let phaseKey: FeatureTypeKey | "" = ""
  for (const key of LAYER_KEYS) {
    const entries = state[key]
    if (entries?.some((f) => f.dbId === dbId)) {
      phaseKey = key
      break
    }
  }

  if (!phaseKey) return

  try {
    await apiFetch(`/api/features/${dbId}`, { method: "DELETE" })

    recordDelete(entry, phaseKey)

    const featuresStore = useFeaturesStore()
    featuresStore.remove(entry.id)
    layerStore.removeFeature(phaseKey, dbId)

    if (phaseKey === "roads") {
      updateEndpointMarkers()
    }

    showToast(t("map_feature_deleted"), "success")
  } catch (err) {
    showToast(t("map_delete_failed", { error: t(getUserMessageKey(err)) }), "error")
  }
}
