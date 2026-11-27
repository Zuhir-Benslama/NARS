// ─── CONTEXT MENU ACTIONS ─────────────────────────────────────────────────────
// enableEditGeometry, editFeatureInfo, removeFeature, and helpers.

import { apiFetch } from "../../api"
import { useSelectionStore } from "../../stores/selectionStore"
import { openEditModal } from "../../stores/modalStore"
import { PHASES } from "../../phases"

import { useFeaturesStore } from "../../stores/featuresStore"
import { useLayerStore, LAYER_KEYS } from "../../stores/layerStore"
import { showToast, showConfirm } from "../../lib/toast"
import type { FeatureData, FeatureTypeKey } from "../../types"
import { getUserMessageKey } from "../../lib/errors"
import { plainCityCenterPlacementError } from "../../lib/city-center"
import { t } from "../../i18n"
import { displayLabelFor } from "../features/feature-labels"
import { recordDelete } from "../undo"
import { enableEditMode } from "../draw/draw-events"
import type { LayerEntry } from "../../types"

import { debugError } from "../../utils/debug"
import { updateEndpointMarkers } from "../roads/road-directions"
import {
  renumberEntranceAfterChange,
  renumberSpecialFeatureByDbId,
  clearSpecialNumberingForFeature,
  specialModeForFeature,
} from "../special-numbering"
import { loadFromDatabase } from "../features/loader-db"
import { updateSelectionHighlight } from "../core/state"

// ─── LOOKUP ───────────────────────────────────────────────────────────────────

export function findLayerEntryByDbId(dbId: string): LayerEntry | null {
  return useLayerStore().getFeature(dbId)
}

// ─── EDIT GEOMETRY ────────────────────────────────────────────────────────────

export function enableEditGeometry(dbId: string): void {
  const entry = findLayerEntryByDbId(dbId)
  if (!entry) {
    showToast(t("map_feature_not_found"), "error")
    return
  }

  if (entry.type === "circle") {
    editFeatureInfo(dbId).catch((err) => debugError("[EDIT] editFeatureInfo:", err))
    return
  }

  // The context menu already names the feature to edit, so adopt it as the
  // selection. Previously this refused whenever a DIFFERENT feature happened
  // to be selected (left-click to inspect, then right-click another road):
  // "Edit geometry" did nothing but show a hint toast, which looked like the
  // edit mode was broken.
  useSelectionStore().setSelectedFeatureDbId(dbId)
  updateSelectionHighlight(dbId)

  enableEditMode(entry.id)
    .then(() => showToast(t("map_edit_mode_hint"), "info"))
    .catch((err) => {
      debugError("[EDIT] enableEditMode:", err)
      showToast(t("map_edit_mode_unavailable"), "error")
    })
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

  // Changing a roundabout into a plain city center must respect the same
  // 0-or-1-per-urban-area rule as drawing a new one.
  const mergedData = { ...entry.data, ...result } as FeatureData
  if (
    entry.data.type === "cityCenter" &&
    (mergedData.cityCenterTypeKey ?? "city_center") !== "roundabout"
  ) {
    const state = useLayerStore().$state
    const error = plainCityCenterPlacementError(
      { lat: mergedData.lat, lng: mergedData.lng },
      state.areas ?? [],
      state.cityCenter ?? [],
      dbId,
    )
    if (error) {
      showToast(
        t(
          error === "outside_urban_area"
            ? "alert_cc_must_be_in_urban_area"
            : "map_city_center_exists",
        ),
        "error",
      )
      return
    }
  }

  try {
    await apiFetch(`/api/features/${dbId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      // `label` is sent at the top level as well as inside `data`: the API's
      // FeatureUpdateRequest binds Label separately and only rewrites the
      // label column when it is non-null (FeatureService keeps the old value
      // otherwise), so omitting it left e.g. roads.label stuck at ''.
      body: JSON.stringify({ label: result.label, data: { ...entry.data, ...result } }),
    })

    useLayerStore().updateFeature(entry.data.type, dbId, result)

    // Rebuild the map so localized labels (subtype + name) follow the edit
    // and the currently selected language.
    await loadFromDatabase()

    if (specialModeForFeature(entry.data.type, entry.data)) {
      await renumberSpecialFeatureByDbId(dbId)
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

  const phase = PHASES.find((p) => p.key === entry.data.type)
  const label = phase ? displayLabelFor(phase, entry.data) : entry.data.label
  const confirmed = await showConfirm(t("map_delete_confirm", { label }))
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
    if (!String(dbId).startsWith("local_")) {
      await apiFetch(`/api/features/${dbId}`, { method: "DELETE" })
    }

    recordDelete(entry, phaseKey)

    const featuresStore = useFeaturesStore()
    featuresStore.remove(entry.id)
    layerStore.removeFeature(phaseKey, dbId)

    if (phaseKey === "roads") {
      updateEndpointMarkers()
    }

    const ed = entry.data as {
      entranceTypeKey?: string
      roadDbId?: string
      numberingMode?: "cite" | "roundabout" | "square"
      numberingFeatureId?: string
      numberingStart?: { lat: number; lng: number }
    }
    if (
      phaseKey === "houseEntrances" &&
      (ed.entranceTypeKey === "main_entrance" || ed.numberingMode)
    ) {
      // The delete succeeded, but stale numbers must not be reported as a
      // clean finish when the recompute fails.
      if ((await renumberEntranceAfterChange(ed)) === "failed") {
        showToast(t("map_delete_numbering_warning"), "warning")
        return
      }
    }
    if (
      (phaseKey === "districts" || phaseKey === "cityCenter" || phaseKey === "publicSpaces") &&
      specialModeForFeature(entry.data.type, entry.data)
    ) {
      // Entrances numbered from this feature must not keep pointing at a
      // deleted reference geometry.
      await clearSpecialNumberingForFeature(dbId)
    }

    showToast(t("map_feature_deleted"), "success")
  } catch (err) {
    showToast(t("map_delete_failed", { error: t(getUserMessageKey(err)) }), "error")
  }
}

// ─── REMOVE ALL ROADS ─────────────────────────────────────────────────────────
// Bulk-deletes every road of the current user (backend cascades their house
// entrances). No undo: like the "clear" endpoint this is a destructive admin
// action guarded only by the confirm dialog.

export async function removeAllRoads(): Promise<void> {
  const layerStore = useLayerStore()
  const count = layerStore.roads.length
  if (count === 0) {
    showToast(t("ctx_roads_none"), "info")
    return
  }

  const selectionStore = useSelectionStore()
  const selectedEntry =
    selectionStore.selectedFeatureDbId !== null
      ? findLayerEntryByDbId(selectionStore.selectedFeatureDbId)
      : null

  const confirmed = await showConfirm(t("ctx_roads_clear_confirm", { count }))
  if (!confirmed) return

  try {
    await apiFetch("/api/features/clear-roads", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ confirm: true }),
    })

    layerStore.clearLayer("roads")
    useFeaturesStore().removeAllPhase("roads")
    if (selectedEntry?.data.type === "roads") {
      selectionStore.setSelectedFeatureDbId(null)
    }
    updateEndpointMarkers()

    showToast(t("ctx_roads_removed_all"), "success")
  } catch (err) {
    showToast(t("map_delete_failed", { error: t(getUserMessageKey(err)) }), "error")
  }
}
