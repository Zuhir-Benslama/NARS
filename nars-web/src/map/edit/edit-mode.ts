// ─── EDIT MODE (ORCHESTRATOR) ─────────────────────────────────────────────────
// Enable/disable edit mode, manage Geoman integration, and re-export
// commit/cancel from edit-commit for backward compatibility.

import type { GeoJsonImportFeature } from "@geoman-io/maplibre-geoman-free"
import type { LatLng } from "../../types"
import { getCtx } from "../core/state"
import { ensureGeoman } from "../map-init"
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
  // Load the geoman bundle on demand the first time the user edits a feature.
  await ensureGeoman()
  const { geoman } = getCtx()
  if (!geoman) return

  if (featureId) {
    const entry = findLayerEntryByFeatureId(featureId)
    if (entry) {
      setActiveEditEntry(entry)
      const d = entry.data as { coordinates?: LatLng[]; lat?: number; lng?: number }
      setActiveEditCoordsSnapshot(
        d.coordinates
          ? d.coordinates.map((c) => ({ lat: c.lat, lng: c.lng }))
          : d.lat != null && d.lng != null
            ? [{ lat: d.lat, lng: d.lng }]
            : null,
      )
      const gj = buildGeomanImportFeature(entry)
      if (gj) {
        // Clean up any stale Geoman features for this dbId left over from a
        // previous (possibly incompletely torn-down) edit session, so the
        // import below creates a single fresh, draggable marker.
        try {
          const all = (await geoman.features.getAll()) as {
            features?: Array<{ id?: string; properties?: { dbId?: string } }>
          }
          for (const f of all?.features ?? []) {
            if (f.properties?.dbId === entry.dbId && f.id) {
              try {
                await geoman.features.delete(f.id)
              } catch {
                // ignore
              }
            }
          }
        } catch {
          // ignore
        }

        try {
          const result = await geoman.features.importGeoJson(gj as GeoJsonImportFeature, {
            overwrite: true,
          })
          const added = (result as { addedFeatures?: Array<{ id?: string }> } | undefined)
            ?.addedFeatures?.[0]
          let geomanId = added?.id ?? null
          if (!geomanId) {
            try {
              const all2 = (await geoman.features.getAll()) as {
                features?: Array<{ id?: string; properties?: { dbId?: string } }>
              }
              const match = all2?.features?.find((f) => f.properties?.dbId === entry.dbId)
              geomanId = match?.id ?? null
            } catch {
              // ignore
            }
          }
          setActiveGeomanFeatureId(geomanId)
        } catch (err) {
          debugError("Geoman importGeoJson failed:", err)
        }
      }
    }
  }

  disableCrosshair()
  disableSnapping()

  const ctx = getCtx()
  if (ctx.map) ctx.map.getCanvas().style.cursor = "move"

  await geoman.enableGlobalEditMode()
  setEditModeActive(true)
  useEditStore().setIsEditMode(true)
  setSnapExclude(featureId ?? null)

  patchMarkerPointerSnap(featureId ?? null)
}
