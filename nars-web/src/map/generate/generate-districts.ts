// ─── GENERATE DISTRICTS ───────────────────────────────────────────────────────
// Context-menu action for the districts phase: cuts the commune's urban areas
// along its primary road network and writes the pieces as pending district
// drafts for review.
//
// Deliberately standalone. This phase is PostGIS-only work over data the user
// has already mapped — the urban areas and the boulevards/avenues — so it needs
// no satellite imagery and no segmentation pass. It used to be reachable only
// as the tail of a road-generation job, which meant re-cutting districts forced
// a full re-segmentation of the commune. Calling POST /api/generation/districts
// directly makes the districts phase independent of the roads phase.

import { useAppStore } from "../../stores/appStore"
import { useGenerationStore } from "../../stores/generationStore"
import { t } from "../../i18n"
import { showToast } from "../../lib/toast"
import { getUserMessageKey, isNarsError } from "../../lib/errors"
import { debugError } from "../../utils/debug"
import { GEN_CONFIG } from "../../config"
import { generateDistricts as requestDistricts } from "../../api/generation"

const { detect, done } = GEN_CONFIG.progressMilestones

export interface GenerateDistrictsResult {
  created: number
  absorbedSlivers: number
}

/**
 * The API rejects with 400 when the commune's mapped data cannot be cut (no
 * urban area, or no boulevard/avenue to cut along). That is an expected,
 * actionable state rather than a failure, and the API's wording is
 * server-side English — so it is reported through a localized key instead of
 * echoing the raw response body (see getUserMessageKey).
 */
function isMissingInputError(err: unknown): boolean {
  return isNarsError(err) && err.context.status === 400
}

/**
 * Runs the districts phase for the signed-in user's commune. Returns the
 * created/absorbed counts, or null when it was skipped or failed (details
 * reported via toasts).
 */
export async function generateDistrictsFromUrbanAreas(): Promise<GenerateDistrictsResult | null> {
  const communeId = useAppStore().user?.commune?.id ?? null
  if (communeId == null) {
    showToast(t("gen_roads_no_commune"), "error")
    return null
  }

  // Shares the road-generation lock so a districts run cannot be started while
  // a road job is mid-flight (or vice versa) — both write draft rows for the
  // same commune.
  const generation = useGenerationStore()
  if (!generation.begin("gen_districts_stage_cut")) {
    showToast(t("gen_districts_in_progress"), "warning")
    return null
  }

  try {
    generation.setProgress(detect, "gen_districts_stage_cut")

    const result = await requestDistricts(communeId)
    const created = result.districts.length

    if (created > 0) {
      showToast(t("gen_districts_done", { count: created }), "success")
    } else {
      showToast(t("gen_districts_none"), "info")
    }

    generation.setProgress(done, "gen_roads_stage_done")
    generation.complete()
    return { created, absorbedSlivers: result.absorbedSlivers }
  } catch (err) {
    generation.abort()
    debugError("[GEN-DISTRICTS]", err)
    if (isMissingInputError(err)) {
      showToast(t("gen_districts_no_input"), "info")
      return null
    }
    showToast(t("gen_districts_failed", { error: t(getUserMessageKey(err)) }), "error")
    return null
  }
}
