// ─── UNDO SYSTEM — DELETED FEATURES ONLY ──────────────────────────────────────
// Tracks deleted features so Ctrl+Z can restore them.
// During drawing, right-click removes the last vertex (handled by Geoman).

import { apiFetch } from "../api"
import { showToast } from "../lib/toast"
import { syncCounts } from "../store"
import { useLayerStore } from "../stores/layerStore"
import type { LayerState } from "../stores/layerStore"
import { featuresStore } from "./core/state"
import { toApiSaveShape } from "./features/features"
import { PHASES } from "../phases"
import { computeCircleRing, closeRing } from "./rendering/geometry"
import { debugLog, debugError, debugWarn } from "../utils/debug"
import type { LayerEntry } from "../types"

// ─── UNDO STACK ───────────────────────────────────────────────────────────────

interface DeletedFeature {
  entry: LayerEntry
  phaseKey: string
}

const undoStack: DeletedFeature[] = []

export function hasUndo(): boolean {
  return undoStack.length > 0
}

export function getUndoLabel(): string | null {
  const last = undoStack[undoStack.length - 1]
  if (!last) return null
  return `Restore "${last.entry.data.label}"`
}

// ─── RECORD ───────────────────────────────────────────────────────────────────

/** Call BEFORE a feature is deleted. Captures the entry for Ctrl+Z restore. */
export function recordDelete(entry: LayerEntry, phaseKey: string): void {
  undoStack.push({ entry, phaseKey })
}

// ─── UNDO (Ctrl+Z) ───────────────────────────────────────────────────────────

export async function undo(): Promise<void> {
  const action = undoStack.pop()
  if (!action) {
    showToast("Nothing to restore.", "info")
    return
  }

  try {
    const { entry, phaseKey } = action

    // Re-create the feature via the API — it gets a new dbId
    const shape = toApiSaveShape(entry.data)
    if (!shape) throw new Error("Could not determine feature type")

    const json = await apiFetch("/api/save", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        type: shape.type,
        layer: shape.layer,
        label: entry.data.label,
        data: entry.data,
      }),
    }).then((r) => r.json())
    const newDbId = json.id as string

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
    const state = layerStore.$state as LayerState

    if (state[phaseKey as keyof LayerState]) {
      ;(state[phaseKey as keyof LayerState] as LayerEntry[]).push(restoredEntry)
    }

    // Repair cross-references: update any secondary entrances that pointed
    // to the old main entrance DB ID to now point to the new one.
    if (phaseKey === "houseEntrances") {
      const oldDbId = entry.dbId
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

    const phase = PHASES.find((p) => p.key === phaseKey)
    const style: Record<string, unknown> = {
      fillColor: phase?.color ?? "#8e44ad",
      fillOpacity: 0.1,
      lineColor: phase?.color ?? "#8e44ad",
      lineWidth: 2,
      circleColor: phase?.color ?? "#8e44ad",
      circleRadius: 8,
      textColor: "#333333",
    }

    const geometry = entryDataToGeometry(entry.data, entry.type)
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

    syncCounts()
    showToast(`Restored "${entry.data.label}".`, "success")
  } catch (err) {
    debugError("[UNDO] Restore failed:", err)
    showToast("Failed to restore feature: " + (err as Error).message, "error")
  }
}

// ─── GEOMETRY HELPERS ─────────────────────────────────────────────────────────

function entryDataToGeometry(
  data: import("../types").FeatureData,
  type: "polygon" | "line" | "circle" | "marker",
): GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon {
  if (type === "marker") {
    return {
      type: "Point",
      coordinates: [data.lng ?? 0, data.lat ?? 0],
    } as GeoJSON.Point
  }
  // City center: restore as LineString circle ring (simple outline)
  if (type === "circle" && data.lat != null && data.lng != null && data.radius) {
    const ring = closeRing(computeCircleRing(data.lat, data.lng, data.radius))
    return { type: "LineString", coordinates: ring } as GeoJSON.LineString
  }
  if (data.coordinates && data.coordinates.length > 0) {
    const coords = data.coordinates.map((c) => [c.lng, c.lat] as [number, number])
    if (type === "line") {
      return { type: "LineString", coordinates: coords } as GeoJSON.LineString
    }
    return { type: "Polygon", coordinates: [closeRing(coords)] } as GeoJSON.Polygon
  }
  // Fallback for points (including circles without radius data)
  return {
    type: "Point",
    coordinates: [data.lng ?? 0, data.lat ?? 0],
  } as GeoJSON.Point
}
