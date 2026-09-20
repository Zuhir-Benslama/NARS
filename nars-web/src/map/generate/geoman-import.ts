// ─── GEOMAN IMPORT FOR GENERATED FEATURES ─────────────────────────────────────
// Manually drawn roads are created inside maplibre-geoman during drawing, so
// they carry geoman's layer/feature behavior (styling, vertices, snapping).
// To give AI-generated roads the same treatment, each is imported into
// geoman's features store using the exact mechanism the app already uses when
// editing a saved feature (enableEditMode → buildGeomanImportFeature →
// importGeoJson). The import is best-effort: roads still render from the
// features/layers stores even if geoman is unavailable.

import type { GeoJsonImportFeature } from "@geoman-io/maplibre-geoman-free"
import type { LayerEntry } from "../../types"
import { getCtx } from "../core/state"
import { ensureGeoman } from "../map-init"
import { buildGeomanImportFeature } from "../edit/edit-import"
import { debugError } from "../../utils/debug"

export async function importFeaturesIntoGeoman(entries: LayerEntry[]): Promise<number> {
  const features = entries
    .map(buildGeomanImportFeature)
    .filter((f): f is GeoJSON.Feature => f !== null)
  if (features.length === 0) return 0

  try {
    await ensureGeoman()
  } catch (err) {
    debugError("[GEN-ROADS] Geoman init failed:", err)
    return 0
  }

  const { geoman } = getCtx()
  if (!geoman) return 0

  let imported = 0
  for (const feature of features) {
    try {
      await geoman.features.importGeoJson(feature as unknown as GeoJsonImportFeature, {
        overwrite: true,
      })
      imported += 1
    } catch (err) {
      debugError("[GEN-ROADS] Geoman import failed:", err)
    }
  }
  return imported
}
