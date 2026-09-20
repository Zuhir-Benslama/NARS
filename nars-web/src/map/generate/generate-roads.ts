// ─── GENERATE ROADS ───────────────────────────────────────────────────────────
// Context-menu action for the roads phase: composites the commune's urban-area
// bounds onto a satellite tile, uploads it to nars-segma via the segmentation
// endpoint, then hands the resulting drafts to the backend's generate-roads
// endpoint which validates them against the roads-phase rules and saves the
// valid ones directly. Created roads land in the features/layer stores so they
// render and snap immediately.

import { useAppStore } from "../../stores/appStore"
import { useLayerStore } from "../../stores/layerStore"
import { useFeaturesStore } from "../../stores/featuresStore"
import { useGenerationStore } from "../../stores/generationStore"
import { PHASES } from "../../phases"
import { t } from "../../i18n"
import { showToast } from "../../lib/toast"
import { getUserMessageKey } from "../../lib/errors"
import { debugError } from "../../utils/debug"
import { refreshLayerVisibility } from "../rendering/labels"
import { updateEndpointMarkers } from "../roads/road-directions"
import { renderSatelliteTile } from "./satellite-tiler"
import { importFeaturesIntoGeoman } from "./geoman-import"
import { EDIT_CONFIG, GEN_CONFIG } from "../../config"
import type { LayerEntry } from "../../types"
import type { TileBounds } from "../../api/drafts"
import { segmentTile, generateRoadsFromDraftIds, type GeneratedRoad } from "../../api/drafts"

const { tiles, detect, save, done } = GEN_CONFIG.progressMilestones

/** Area sub-types that count as urban (matches backend FeatureTypes.AreaLayers.Urban). */
const URBAN_AREA_TYPES: ReadonlySet<string> = new Set(["central_urban", "secondary_urban"])

export interface GenerateRoadsResult {
  created: number
  dropped: number
}

/** Union bounds of the commune's urban areas, or null when none are drawn. */
export function urbanAreaBounds(): TileBounds | null {
  const areas = useLayerStore().$state.areas ?? []
  const urban = areas.filter((a) => URBAN_AREA_TYPES.has(a.data.areaTypeKey ?? ""))
  if (urban.length === 0) return null

  let minLon = Infinity
  let minLat = Infinity
  let maxLon = -Infinity
  let maxLat = -Infinity
  for (const area of urban) {
    for (const c of area.data.coordinates ?? []) {
      minLon = Math.min(minLon, c.lng)
      maxLon = Math.max(maxLon, c.lng)
      minLat = Math.min(minLat, c.lat)
      maxLat = Math.max(maxLat, c.lat)
    }
  }
  if (!Number.isFinite(minLon)) return null
  return { minLon, minLat, maxLon, maxLat }
}

async function addGeneratedRoads(roads: GeneratedRoad[]): Promise<void> {
  if (roads.length === 0) return
  const featuresStore = useFeaturesStore()
  const layerStore = useLayerStore()
  const roadPhase = PHASES.find((p) => p.key === "roads")
  const lineColor = roadPhase?.color ?? "#3498db"
  // Render generated roads as the thin geoman-drawn line (the app's geoman
  // `gm_main`/`gm_temporary` line layers are 3px #3498db — see
  // ensureGeomanDrawEdgesVisible), so the roads look like manually drawn ones
  // instead of the thick 8px saved-feature style. The geoman layer sits
  // exactly on this line, keeping a single visible 3px road.
  const lineWidth = EDIT_CONFIG.edgeLineWidth
  const generated: LayerEntry[] = []

  for (const road of roads) {
    const coordinates = road.data.coordinates ?? []
    if (coordinates.length < 2) continue
    if (layerStore.getFeature(road.dbId)) continue

    const id = `feat_${road.dbId}`
    featuresStore.add({
      id,
      geometry: {
        type: "LineString",
        coordinates: coordinates.map((c) => [c.lng, c.lat] as [number, number]),
      },
      properties: {
        dbId: road.dbId,
        phaseKey: "roads",
        label: road.label ?? "",
        geomType: "LineString",
        lineColor,
        lineWidth,
        textColor: "#333333",
      },
    })
    layerStore.addFeature("roads", {
      id,
      dbId: road.dbId,
      data: {
        type: "roads",
        label: road.label ?? "",
        coordinates,
        decisionNumber: "",
        decisionDate: "",
        roadTypeKey: road.layer || "street",
      },
      type: "line",
    })

    const entry = layerStore.getFeature(road.dbId)
    if (entry) generated.push(entry)
  }

  const imported = await importFeaturesIntoGeoman(generated)
  if (generated.length > 0 && imported === 0) {
    showToast(t("gen_roads_geoman_failed"), "warning")
  }

  refreshLayerVisibility()
  updateEndpointMarkers()
}

/**
 * Runs the full generate-roads flow. Returns the created/dropped counts, or
 * null when the flow was skipped/failed (details reported via toasts).
 */
export async function generateRoadsFromUrbanAreas(): Promise<GenerateRoadsResult | null> {
  const communeId = useAppStore().user?.commune?.id ?? null
  if (communeId == null) {
    showToast(t("gen_roads_no_commune"), "error")
    return null
  }

  const bounds = urbanAreaBounds()
  if (!bounds) {
    showToast(t("gen_roads_no_urban_area"), "info")
    return null
  }

  const generation = useGenerationStore()
  if (!generation.begin("gen_roads_stage_tiles")) {
    showToast(t("gen_roads_in_progress"), "warning")
    return null
  }
  generation.setProgress(tiles)

  showToast(t("gen_roads_started"), "info")

  try {
    const tile = await renderSatelliteTile(bounds)
    generation.setProgress(detect, "gen_roads_stage_detect")

    const segment = await segmentTile({ communeId, tile: tile.blob, bounds: tile.bounds })
    if (!segment.draftIds.length) {
      showToast(t("gen_roads_no_detections"), "info")
      generation.complete()
      return { created: 0, dropped: 0 }
    }

    generation.setProgress(save, "gen_roads_stage_save")
    const summary = await generateRoadsFromDraftIds(communeId, segment.draftIds)
    generation.setProgress(done, "gen_roads_stage_done")
    generation.complete()
    await addGeneratedRoads(summary.created)
    showToast(
      t("gen_roads_done", { created: summary.created.length, dropped: summary.dropped }),
      "success",
    )
    return { created: summary.created.length, dropped: summary.dropped }
  } catch (err) {
    generation.abort()
    debugError("[GEN-ROADS]", err)
    showToast(t("gen_roads_failed", { error: t(getUserMessageKey(err)) }), "error")
    return null
  }
}
