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
import { renderSatelliteGrid, splitBoundsAtZoom } from "./satellite-tiler"
import { importFeaturesIntoGeoman } from "./geoman-import"
import { EDIT_CONFIG, GEN_CONFIG, MAP_CONFIG } from "../../config"
import type { LayerEntry } from "../../types"
import type { TileBounds } from "../../api/drafts"
import {
  segmentTile,
  generateRoadsFromDraftIds,
  listDrafts,
  type GeneratedRoad,
} from "../../api/drafts"

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
 * Lists every pending road draft for a commune. Used as a fallback input when
 * a generate run finds no *new* detections (see the no-detections branch of
 * generateRoadsFromUrbanAreas). Pages through the draft queue in chunks since
 * the API caps each page at Pagination.MaxTake (500).
 */
async function listPendingRoadDraftIds(communeId: number): Promise<string[]> {
  const ids: string[] = []
  const take = 500
  for (let skip = 0; ; skip += take) {
    const drafts = await listDrafts({
      communeId,
      featureType: "road",
      status: "pending",
      skip,
      take,
    })
    ids.push(...drafts.map((d) => d.id))
    if (drafts.length < take) break
  }
  return ids
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

  showToast(t("gen_roads_started"), "info")

  try {
    // Segment every chunk at the highest satellite zoom (z18) so a commune
    // wider than the 24x24-tile grid cap is covered by several z18 images
    // instead of falling back to z17 where the model under-detects roads.
    const grids = splitBoundsAtZoom(bounds, MAP_CONFIG.tileMaxZoomSatellite)
    const allDraftIds: string[] = []
    for (let i = 0; i < grids.length; i += 1) {
      generation.setProgress(tiles, i === 0 ? "gen_roads_stage_tiles" : "gen_roads_stage_chunk")
      const tile = await renderSatelliteGrid(grids[i])
      const segment = await segmentTile({ communeId, tile: tile.blob, bounds: tile.bounds })
      allDraftIds.push(...segment.draftIds)
    }

    if (!allDraftIds.length) {
      // Segmentation dedups every detection against the drafts already stored
      // for the commune — including drafts whose roads the user just deleted
      // (clear-roads now reverts accepted road drafts to pending instead of
      // deleting them). So a fresh run after "remove all roads" finds nothing
      // new: the AI would just re-create the same geometry. Fall back to the
      // commune's pending road drafts so the network can be rebuilt from the
      // review queue instead of stalling with "no roads detected".
      const pendingIds = await listPendingRoadDraftIds(communeId)
      if (!pendingIds.length) {
        showToast(t("gen_roads_no_detections"), "info")
        generation.complete()
        return { created: 0, dropped: 0 }
      }
      showToast(t("gen_roads_reusing_drafts"), "info")
      allDraftIds.push(...pendingIds)
    }

    generation.setProgress(detect, "gen_roads_stage_detect")
    const summary = await generateRoadsFromDraftIds(communeId, allDraftIds)
    generation.setProgress(save, "gen_roads_stage_save")
    generation.setProgress(done, "gen_roads_stage_done")
    generation.complete()
    await addGeneratedRoads(summary.created)

    if (summary.dropped > 0) {
      const breakdown = summary.breakdown
      showToast(
        t("gen_roads_done_breakdown", {
          tooShort: breakdown.tooShort,
          lowConfidence: breakdown.lowConfidence,
          turnAngle: breakdown.excessiveTurnAngle,
          outside: breakdown.outsideUrbanArea,
          invalid: breakdown.invalidGeometry,
        }),
        "success",
      )
    }
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
