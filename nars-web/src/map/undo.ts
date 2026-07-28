// ─── UNDO SYSTEM — DELETED FEATURES ONLY ──────────────────────────────────────
// Tracks deleted features so Ctrl+Z can restore them.
// During drawing, right-click removes the last vertex (handled by Geoman).

import { apiFetch } from "../api"
import { getErrorMessage } from "../lib/errors"
import { showToast } from "../lib/toast"
import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import { useUndoStore } from "../stores/undoStore"
import { useFeaturesStore } from "../stores/featuresStore"
import { toApiSaveShape } from "./features/feature-data"
import { PHASES } from "../phases"
import { computeCircleRing, closeRing } from "./rendering/geometry"
import { getDefaultStyle } from "./draw/draw-save"
import { t } from "../i18n"
import { debugLog, debugError, debugWarn } from "../utils/debug"
import type { FeatureTypeKey, LayerEntry } from "../types"

// ─── STATE RESET (for testing & HMR) ──────────────────────────────────────────

export function resetUndoStack(): void {
  useUndoStore().$reset()
}

export function hasUndo(): boolean {
  return useUndoStore().hasUndo
}

export function getUndoLabel(): string | null {
  return useUndoStore().undoLabel
}

// ─── RECORD ───────────────────────────────────────────────────────────────────

/** Call BEFORE a feature is deleted. Captures the entry for Ctrl+Z restore. */
export function recordDelete(entry: LayerEntry, phaseKey: FeatureTypeKey): void {
  useUndoStore().recordDelete(entry, phaseKey)
}

// ─── UNDO (Ctrl+Z) ───────────────────────────────────────────────────────────

export async function undo(): Promise<void> {
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
    const state = layerStore.$state

    if (state[phaseKey]) {
      ;(state[phaseKey] as unknown as LayerEntry[]).push(restoredEntry)
    }

    // Repair cross-references: update any features that pointed to the old
    // database ID to now point to the new one.
    const oldDbId = entry.dbId
    if (phaseKey === "houseEntrances") {
      for (const entrance of state.houseEntrances || []) {
        if (entrance.data.mainEntranceDbId === oldDbId) {
          entrance.data.mainEntranceDbId = newDbId
          entrance.data.mainEntranceLabel = restoredEntry.data.label
          debugLog(
            `[UNDO] Updated secondary entrance "${entrance.data.label}" → mainEntranceDbId ${newDbId}`,
          )
        }
      }
    }

    if (phaseKey === "roads") {
      for (const entrance of state.houseEntrances || []) {
        if (entrance.data.roadDbId === oldDbId) {
          entrance.data.roadDbId = newDbId
          debugLog(`[UNDO] Updated entrance "${entrance.data.label}" → roadDbId ${newDbId}`)
        }
      }
    }

    const phase = PHASES.find((p) => p.key === phaseKey)
    const style = getDefaultStyle(phase?.color ?? "#8e44ad")

    const geometry = entryDataToGeometry(entry.data, entry.type)
    const featuresStore = useFeaturesStore()
    featuresStore.add({
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

    useAppStore().syncCounts()
    showToast(t("map_restored", { label: entry.data.label }), "success")
  } catch (err) {
    debugError("[UNDO] Restore failed:", err)
    showToast(t("map_restore_failed", { error: getErrorMessage(err) }), "error")
  }
}

// ─── GEOMETRY HELPERS ─────────────────────────────────────────────────────────

function entryDataToGeometry(
  data: import("../types").FeatureData,
  type: "polygon" | "line" | "circle" | "marker",
): GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon {
  if (type === "marker") {
    return {
      type: "Point" as const,
      coordinates: [data.lng ?? 0, data.lat ?? 0],
    }
  }
  if (type === "circle" && data.lat != null && data.lng != null && data.radius) {
    const ring = closeRing(computeCircleRing(data.lat, data.lng, data.radius))
    return { type: "LineString" as const, coordinates: ring }
  }
  if (data.coordinates && data.coordinates.length > 0) {
    const coords = data.coordinates.map((c) => [c.lng, c.lat] as [number, number])
    if (type === "line") {
      return { type: "LineString" as const, coordinates: coords }
    }
    return { type: "Polygon" as const, coordinates: [closeRing(coords)] }
  }
  return {
    type: "Point" as const,
    coordinates: [data.lng ?? 0, data.lat ?? 0],
  }
}
