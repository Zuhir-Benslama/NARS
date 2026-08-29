// ─── PHASE NAVIGATION ─────────────────────────────────────────────────────────

import { t } from "../i18n"
import { PHASES } from "../phases"
import { debugWarn } from "../utils/debug"
import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import { showToast } from "../lib/toast"
import { checkDistrictCoverage } from "../lib/validation"
import { buildDrawControl } from "../map/draw/draw-control"
import { ensureGeoman } from "../map/map-init"
import { setDrawingPhase } from "../map/draw/draw-complete"
import { refreshLayerVisibility } from "../map/rendering/labels"
import { computeAndApplyRoadDirections } from "../map/roads/road-directions"
import { savePhase } from "./storage"

export async function navigatePhase(direction: number): Promise<void> {
  const appStore = useAppStore()
  const target = appStore.currentPhase + direction
  if (target < 0 || target >= PHASES.length) return

  const layerStore = useLayerStore()
  const state = layerStore.$state

  if (direction > 0) {
    const from = PHASES[appStore.currentPhase]

    if (from.key === "areas" && (state.areas?.length ?? 0) === 0) {
      showToast(t("alert_at_least_one_urban_area"), "error")
      return
    }
    if (from.key === "districts") {
      const coverage = await checkDistrictCoverage()
      if (!coverage.covered) {
        showToast(t("alert_coverage_error", { message: coverage.message }), "error")
        return
      }
    }
    if (from.key === "roads" && (state.roads?.length ?? 0) === 0) {
      showToast(t("alert_at_least_one_road"), "error")
      return
    }
    if (from.key === "houseEntrances" && (state.houseEntrances?.length ?? 0) === 0) {
      showToast(t("alert_at_least_one_entrance"), "error")
      return
    }

    // When leaving roads phase, auto-orient road directions
    if (from.key === "roads" && (state.roads?.length ?? 0) > 0) {
      await computeAndApplyRoadDirections()
    }
  }

  setPhase(target)
}

export async function goToPhase(target: number): Promise<void> {
  const appStore = useAppStore()
  if (target === appStore.currentPhase) return
  if (target > appStore.currentPhase) {
    for (let i = appStore.currentPhase; i < target; i++) {
      const before = appStore.currentPhase
      await navigatePhase(1)
      if (appStore.currentPhase === before) return
    }
  } else {
    setPhase(target)
  }
}

export function setPhase(index: number): void {
  const appStore = useAppStore()
  appStore.setCurrentPhase(index)
  const communeId = appStore.user?.commune?.id ?? null
  savePhase(index, communeId)
  const phase = PHASES[index]
  setDrawingPhase(phase ?? null)
  if (phase) {
    // User-driven phase navigation: lazily initialize the geoman editor on
    // first use, then arm that phase's draw mode.
    void ensureGeoman()
      .then(() => buildDrawControl(phase))
      .catch((err) => debugWarn("[NAV] Unable to arm draw control:", err))
  }
  refreshLayerVisibility()
}
