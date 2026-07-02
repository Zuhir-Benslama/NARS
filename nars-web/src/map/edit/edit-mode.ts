// ─── EDIT MODE (ORCHESTRATOR) ─────────────────────────────────────────────────
// Enable/disable edit mode, manage Geoman integration, and re-export
// commit/cancel from edit-commit for backward compatibility.

import type { GeoJsonImportFeature } from "@geoman-io/maplibre-geoman-free"
import { ctx } from "../core/state"
import {
  disableCrosshair,
  disableSnapping,
  setSnapExclude,
  setEditModeActive,
} from "../snapping/snapping"
import { debugError } from "../../utils/debug"
import { useEditStore } from "../../stores/editStore"
import {
  findLayerEntryByFeatureId,
  setActiveGeomanFeatureId,
  setActiveEditEntry,
  setActiveEditCoordsSnapshot,
} from "./edit-state"
import { buildGeomanImportFeature } from "./edit-import"
import { patchMarkerPointerSnap } from "./edit-snap"
export {
  isEditMode,
  getActiveEditEntry,
  findLayerEntryByFeatureId,
  suppressGeomanFill,
  ensureGeomanDrawEdgesVisible,
  disableEditMode,
} from "./edit-state"
export { commitEditMode, cancelEditMode } from "./edit-commit"

// ─── ENABLE EDIT MODE ────────────────────────────────────────────────────────

export async function enableEditMode(featureId?: string): Promise<void> {
  if (!ctx.geoman) return

  if (featureId) {
    const entry = findLayerEntryByFeatureId(featureId)
    if (entry) {
      setActiveEditEntry(entry)
      setActiveEditCoordsSnapshot(
        entry.data.coordinates
          ? entry.data.coordinates.map((c) => ({ lat: c.lat, lng: c.lng }))
          : entry.data.lat != null && entry.data.lng != null
            ? [{ lat: entry.data.lat, lng: entry.data.lng }]
            : null,
      )
      const gj = buildGeomanImportFeature(entry)
      if (gj) {
        try {
          const result = await ctx.geoman.features.importGeoJson(gj as GeoJsonImportFeature, {
            overwrite: true,
          })
          const added = (result as { addedFeatures?: Array<{ id?: string }> } | undefined)
            ?.addedFeatures?.[0]
          setActiveGeomanFeatureId(added?.id ?? null)
        } catch (err) {
          debugError("Geoman importGeoJson failed:", err)
        }
      }
    }
  }

  disableCrosshair()
  disableSnapping()

  await ctx.geoman.enableGlobalEditMode()
  setEditModeActive(true)
  useEditStore().isEditMode = true
  setSnapExclude(featureId ?? null)

  patchMarkerPointerSnap(featureId ?? null)
}
