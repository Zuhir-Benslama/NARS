// ─── GENERATE ROADS ───────────────────────────────────────────────────────────
// Context-menu action for the roads phase: composites the commune's urban-area
// bounds onto a satellite tile grid, then drives the ASYNC generation queue:
// the client creates a job with the full grid split, uploads each rendered
// raster chunk, and polls until the worker pool (nars-api) has segmented every
// chunk and run the roads-phase acceptance. Created roads land in the
// features/layer stores so they render and snap immediately.
//
// Uploads happen per-chunk and the job record lives in the DB, so the run is
// resumable server-side across browser/pod restarts; a failed mid-way upload
// cancels the job (a half-uploaded grid would otherwise park the job in
// 'active' forever waiting for the never-arriving chunk).

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
import { renderSatelliteGrid, splitBoundsAtZoom, gridBounds } from "./satellite-tiler"
import type { TileGrid } from "./satellite-tiler"
import { importFeaturesIntoGeoman } from "./geoman-import"
import { EDIT_CONFIG, GEN_CONFIG, MAP_CONFIG } from "../../config"
import type { LayerEntry } from "../../types"
import type { TileBounds } from "../../api/drafts"
import { generateRoadsFromDraftIds, listDrafts, type GeneratedRoad } from "../../api/drafts"
import {
  createGenerationJob,
  getGenerationJob,
  uploadGenerationChunkRaster,
  cancelGenerationJob,
  GENERATION_JOB_STATUS,
  toGenerationGrid,
  type GenerationJobView,
} from "../../api/generation"

const { tiles, detect, save, done } = GEN_CONFIG.progressMilestones

/** Area sub-types that count as urban (matches backend FeatureTypes.AreaLayers.Urban). */
const URBAN_AREA_TYPES: ReadonlySet<string> = new Set(["central_urban", "secondary_urban"])

/** Poll cadence while the worker pool is segmenting / accepting. */
const JOB_POLL_INTERVAL_MS = 2_500

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
 * Lists every pending road draft for a commune. Used as the fallback input when
 * a generation run finds no *new* detections (see the no-detections branch of
 * generateRoadsFromUrbanAreas): after clear-roads reverts accepted drafts to
 * pending, re-running re-detects the same geometry, so the queue is reused to
 * rebuild the network instead of stalling. Pages through the draft queue in
 * chunks since the API caps each page at Pagination.MaxTake (500).
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

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/** Splits every grid of a job the same way the order-independent raster renderer does. */
function jobGrids(bounds: TileBounds): TileGrid[] {
  return splitBoundsAtZoom(bounds, MAP_CONFIG.tileMaxZoomSatellite)
}

/** Cancels a half-uploaded job best-effort so it never parks in 'active' forever. */
async function cancelJobQuietly(jobId: string): Promise<void> {
  try {
    await cancelGenerationJob(jobId)
  } catch {
    // The job may already be terminal; nothing actionable client-side.
  }
}

/**
 * Advances the progress bar from the job's server-side state. Segment phase is
 * measured by doneChunks/totalChunks; the acceptance pass (stage 'accepting')
 * is its own step because it runs for the whole commune at once.
 */
function reflectJobProgress(job: GenerationJobView): void {
  const percent = Math.round(job.progress * 100)
  if (job.status === GENERATION_JOB_STATUS.accepting) {
    useGenerationStore().setProgress(detect, "gen_roads_stage_detect")
  } else {
    useGenerationStore().setProgress(percent, "gen_roads_stage_chunk")
  }
}

/**
 * Runs the full generate-roads flow against the async queue. Returns the
 * created/dropped counts, or null when the flow was skipped/failed (details
 * reported via toasts).
 *
 * Roads only: the districts phase is a separate action (generate-districts.ts)
 * that posts to its own endpoint, so it is never requested here.
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

  let jobId: string | null = null
  try {
    // Segment every chunk at the highest satellite zoom (z18) so a commune
    // wider than the 24x24-tile grid cap is covered by several z18 images
    // instead of falling back to z17 where the model under-detects roads.
    const grids = jobGrids(bounds)
    const job = await createGenerationJob(
      communeId,
      grids.map((grid, index) =>
        toGenerationGrid(String(index), grid.zoom, grid, gridBounds(grid)),
      ),
    )
    jobId = job.id

    // 1. Upload each rendered raster chunk. Sorted like the grid, so chunk i is
    //    the i-th grid. A failure anywhere cancels the job (see header).
    for (let i = 0; i < grids.length; i += 1) {
      const chunk = job.chunks[i]
      if (!chunk) throw new Error(`Job ${job.id} is missing chunk ${i}.`)
      generation.setProgress(
        i === 0 ? tiles : Math.round((i / grids.length) * 100),
        i === 0 ? "gen_roads_stage_tiles" : "gen_roads_stage_chunk",
      )
      const tile = await renderSatelliteGrid(grids[i]!)
      await uploadGenerationChunkRaster(job.id, chunk.id, tile.blob, `${chunk.chunkKey}.jpg`)
      generation.setProgress(Math.round(((i + 1) / grids.length) * 100), "gen_roads_stage_chunk")
    }

    // 2. Poll until the worker pool settles. Segmentation dominates the wall
    //    clock (z18 inference windows); acceptance adds rule checks + DB writes.
    let view = await getGenerationJob(job.id)
    while (
      view.status !== GENERATION_JOB_STATUS.done &&
      view.status !== GENERATION_JOB_STATUS.failed &&
      view.status !== GENERATION_JOB_STATUS.cancelled
    ) {
      await sleep(JOB_POLL_INTERVAL_MS)
      view = await getGenerationJob(job.id)
      reflectJobProgress(view)
    }

    if (view.status === GENERATION_JOB_STATUS.failed) {
      throw new Error(view.error ?? "road generation failed")
    }
    if (view.status === GENERATION_JOB_STATUS.cancelled) {
      showToast(t("gen_roads_cancelled"), "info")
      generation.complete()
      return { created: 0, dropped: 0 }
    }

    // 3. Result-shaped GenerateRoadsResponse (dropped/created/breakdown).
    const created: GeneratedRoad[] = [...(view.result?.created ?? [])]
    let dropped = view.result?.dropped ?? 0
    const breakdown = view.result?.breakdown ?? {
      tooShort: 0,
      lowConfidence: 0,
      excessiveTurnAngle: 0,
      outsideUrbanArea: 0,
      tooClose: 0,
      invalidGeometry: 0,
    }

    // 4. Fallback when the run produced no roads but the queue still holds
    //    accept-ready drafts (clear-roads re-created pending rows; segmentation
    //    dedup blocks a second re-detection). A single acceptance pass on the
    //    pending ids rebuilds the network.
    if (created.length === 0) {
      const pendingIds = await listPendingRoadDraftIds(communeId)
      if (pendingIds.length) {
        showToast(t("gen_roads_reusing_drafts"), "info")
        generation.setProgress(save, "gen_roads_stage_save")
        const fallback = await generateRoadsFromDraftIds(communeId, pendingIds)
        created.push(...fallback.created)
        dropped += fallback.dropped
        breakdown.tooShort += fallback.breakdown.tooShort
        breakdown.lowConfidence += fallback.breakdown.lowConfidence
        breakdown.excessiveTurnAngle += fallback.breakdown.excessiveTurnAngle
        breakdown.outsideUrbanArea += fallback.breakdown.outsideUrbanArea
        breakdown.tooClose += fallback.breakdown.tooClose
        breakdown.invalidGeometry += fallback.breakdown.invalidGeometry
      } else {
        showToast(t("gen_roads_no_detections"), "info")
        generation.complete()
        return { created: 0, dropped: 0 }
      }
    }

    generation.setProgress(save, "gen_roads_stage_save")
    generation.setProgress(done, "gen_roads_stage_done")
    generation.complete()
    await addGeneratedRoads(created)

    if (dropped > 0) {
      showToast(
        t("gen_roads_done_breakdown", {
          tooShort: breakdown.tooShort,
          lowConfidence: breakdown.lowConfidence,
          turnAngle: breakdown.excessiveTurnAngle,
          outside: breakdown.outsideUrbanArea,
          tooClose: breakdown.tooClose,
          invalid: breakdown.invalidGeometry,
        }),
        "success",
      )
    }
    showToast(t("gen_roads_done", { created: created.length, dropped }), "success")
    return { created: created.length, dropped }
  } catch (err) {
    if (jobId) await cancelJobQuietly(jobId)
    generation.abort()
    debugError("[GEN-ROADS]", err)
    showToast(t("gen_roads_failed", { error: t(getUserMessageKey(err)) }), "error")
    return null
  }
}
