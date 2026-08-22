// ─── UNDO SYSTEM — DELETED FEATURES ONLY ──────────────────────────────────────
// Tracks deleted features so Ctrl+Z can restore them.
// During drawing, right-click removes the last vertex (handled by Geoman).

import { apiFetch } from "../api"
import { getUserMessageKey } from "../lib/errors"
import { showToast } from "../lib/toast"
import { useLayerStore } from "../stores/layerStore"
import { useUndoStore, MAX_UNDO_ENTRIES } from "../stores/undoStore"
import { useFeaturesStore } from "../stores/featuresStore"
import { toApiSaveShape, featureDataToGeometry } from "./features/feature-data"
import { PHASES } from "../phases"
import { getDefaultStyle } from "./draw/draw-save"
import { t } from "../i18n"
import { debugLog, debugError, debugWarn } from "../utils/debug"
import type { FeatureTypeKey, LayerEntry, HouseEntranceFeatureData } from "../types"
import type { MaplibreFeature } from "./core/state"

// ─── STATE RESET (for testing & HMR) ──────────────────────────────────────────

export function resetUndoStack(): void {
  useUndoStore().$reset()
}

// ─── RECORD ───────────────────────────────────────────────────────────────────

/**
 * When the undo stack overflows, the oldest deleted feature is evicted and
 * its deletion becomes permanent (no Ctrl+Z recovery). Any surviving
 * entrances still referencing its DB ID must be detached now — otherwise they
 * dangle forever, whereas within the restorable window the restore path
 * repairs references instead. This is the delete-side counterpart of the
 * repair logic in undo().
 */
async function detachReferencesForEvicted(evicted: {
  entry: LayerEntry
  phaseKey: FeatureTypeKey
}): Promise<void> {
  const layerStore = useLayerStore()
  const oldDbId = evicted.entry.dbId

  const fixes: { entrance: LayerEntry<HouseEntranceFeatureData> }[] = []
  for (const entrance of layerStore.$state.houseEntrances || []) {
    if (evicted.phaseKey === "roads" && entrance.data.roadDbId === oldDbId) {
      layerStore.updateFeature("houseEntrances", entrance.dbId, { roadDbId: undefined })
      fixes.push({ entrance })
    }
    if (evicted.phaseKey === "houseEntrances" && entrance.data.mainEntranceDbId === oldDbId) {
      layerStore.updateFeature("houseEntrances", entrance.dbId, {
        mainEntranceDbId: undefined,
        mainEntranceLabel: undefined,
      })
      fixes.push({ entrance })
    }
  }
  if (fixes.length === 0) return

  debugLog(
    `[UNDO] Evicted delete of "${evicted.entry.data.label}" — detaching ${fixes.length} cross-reference(s)`,
  )
  try {
    const results = await Promise.allSettled(
      fixes.map(({ entrance }) =>
        apiFetch(`/api/features/${entrance.dbId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ data: entrance.data }),
        }),
      ),
    )
    const failures = results.filter((r) => r.status === "rejected").length
    if (failures > 0) debugError(`Failed to persist ${failures} detached reference(s).`)

    useFeaturesStore().batchUpdate(
      fixes.map(({ entrance }) => ({
        id: entrance.id,
        properties: { roadDbId: "", mainEntranceDbId: "", mainEntranceLabel: "" },
      })),
    )
  } catch (err) {
    debugError("Failed to detach cross-references for evicted undo entry:", err)
  }
}

/** Call BEFORE a feature is deleted. Captures the entry for Ctrl+Z restore. */
export function recordDelete(entry: LayerEntry, phaseKey: FeatureTypeKey): void {
  const store = useUndoStore()
  if (store.undoStack.length >= MAX_UNDO_ENTRIES) {
    const evicted = store.shiftUndo()
    if (evicted) void detachReferencesForEvicted(evicted)
  }
  store.recordDelete(entry, phaseKey)
}

// ─── UNDO (Ctrl+Z) ───────────────────────────────────────────────────────────

let _undoInProgress = false

export async function undo(): Promise<void> {
  if (_undoInProgress) {
    debugWarn("[UNDO] Undo already in progress — skipping")
    return
  }
  _undoInProgress = true
  try {
    const action = useUndoStore().popUndo()
    if (!action) {
      showToast(t("map_nothing_to_restore"), "info")
      return
    }

    try {
      const { entry, phaseKey } = action

      // Re-create the feature via the API — it gets a new dbId
      const shape = toApiSaveShape(entry.data)

      const json = await apiFetch("/api/features", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          type: shape.type,
          layer: shape.layer,
          label: entry.data.label,
          data: entry.data,
        }),
      }).then((r) => r.json())
      const newDbId = json.id as string | undefined
      if (!newDbId) {
        showToast(t("map_restore_no_id"), "error")
        return
      }

      const newId = crypto.randomUUID()

      const restoredEntry: LayerEntry = {
        ...entry,
        id: newId,
        dbId: newDbId,
      }

      // ⚠️ Dev warning: restored feature gets a NEW database ID.
      // Any cross-references (e.g. secondary entrances → main entrance)
      // pointing to the old ID will be broken.
      debugWarn(
        `[UNDO] Restored "${entry.data.label}" with new DB ID ${newDbId} (old: ${entry.dbId})`,
      )

      const layerStore = useLayerStore()
      layerStore.addFeature(phaseKey, restoredEntry)
      const state = layerStore.$state

      // Repair cross-references: update any features that pointed to the old
      // database ID to now point to the new one.
      const oldDbId = entry.dbId
      const repairedEntrances: LayerEntry<HouseEntranceFeatureData>[] = []
      if (phaseKey === "houseEntrances") {
        for (const entrance of state.houseEntrances || []) {
          if (entrance.data.mainEntranceDbId === oldDbId) {
            layerStore.updateFeature("houseEntrances", entrance.dbId, {
              mainEntranceDbId: newDbId,
              mainEntranceLabel: restoredEntry.data.label,
            })
            repairedEntrances.push(entrance)
            debugLog(
              `[UNDO] Updated secondary entrance "${entrance.data.label}" → mainEntranceDbId ${newDbId}`,
            )
          }
        }
      }

      if (phaseKey === "roads") {
        for (const entrance of state.houseEntrances || []) {
          if (entrance.data.roadDbId === oldDbId) {
            layerStore.updateFeature("houseEntrances", entrance.dbId, { roadDbId: newDbId })
            repairedEntrances.push(entrance)
            debugLog(`[UNDO] Updated entrance "${entrance.data.label}" → roadDbId ${newDbId}`)
          }
        }
      }

      // Persist the reference repair — the server still stores the old (deleted)
      // ID, so the in-memory fix alone would be lost on reload.
      if (repairedEntrances.length > 0) {
        const results = await Promise.allSettled(
          repairedEntrances.map((entrance) =>
            apiFetch(`/api/features/${entrance.dbId}`, {
              method: "PUT",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ data: entrance.data }),
            }),
          ),
        )
        const failures = results.filter((r) => r.status === "rejected").length
        if (failures > 0) {
          debugError(`[UNDO] Failed to persist ${failures} cross-reference repair(s).`)
          showToast(t("map_restore_refs_warning"), "warning")
        }

        // Re-render the map source so the repaired cross-references are
        // reflected in the GeoJSON properties without a reload.
        const featuresStore = useFeaturesStore()
        featuresStore.batchUpdate(
          repairedEntrances.map((entrance) => {
            const properties: Partial<MaplibreFeature["properties"]> = {}
            if (phaseKey === "houseEntrances") {
              properties.mainEntranceDbId = entrance.data.mainEntranceDbId
              properties.mainEntranceLabel = entrance.data.mainEntranceLabel
            }
            if (phaseKey === "roads") {
              properties.roadDbId = entrance.data.roadDbId
            }
            return { id: entrance.id, properties }
          }),
        )
      }

      const phase = PHASES.find((p) => p.key === phaseKey)
      const style = getDefaultStyle(phase?.color ?? "#8e44ad")

      const geometry = entryDataToGeometry(entry.data, entry.type)
      useFeaturesStore().add({
        id: newId,
        geometry,
        properties: {
          dbId: newDbId,
          phaseKey,
          label: entry.data.label,
          geomType: geometry.type,
          ...style,
        },
      })

      showToast(t("map_restored", { label: entry.data.label }), "success")
    } catch (err) {
      debugError("[UNDO] Restore failed:", err)
      showToast(t("map_restore_failed", { error: t(getUserMessageKey(err)) }), "error")
    }
  } finally {
    _undoInProgress = false
  }
}

// ─── GEOMETRY HELPERS ─────────────────────────────────────────────────────────

function entryDataToGeometry(
  data: import("../types").FeatureData,
  type: "polygon" | "line" | "circle" | "marker",
): GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon {
  return featureDataToGeometry(data, type)
}
