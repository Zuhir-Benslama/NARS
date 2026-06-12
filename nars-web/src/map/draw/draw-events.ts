// ─── DRAW EVENTS ORCHESTRATOR ─────────────────────────────────────────
// Wires up draw handlers, marker snapping patch, and reactive phase watching.

import { watch } from "vue"
import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { debugWarn } from "../../utils/debug"

// Re-export state and functions used by other modules
export {
  isEditMode,
  enableEditMode,
  commitEditMode,
  cancelEditMode,
  suppressGeomanFill,
} from "../edit/edit-mode"
export { getFeatureStyle } from "./draw-save"

// ─── IMPORTS FOR REGISTRATION ───────────────────────────────────────

import { ctx } from "../core/state"
import { setRepatchMarkerPointer } from "./draw-state"
import { repatchMarkerPointer } from "./draw-marker-patch"
import { registerDrawHandlers } from "./draw-handlers"
import { patchGeomanMarkerPointerSnap } from "./draw-marker-patch"
import { installSnapInterceptors } from "../snapping/snapping"
import { setDrawingPhase } from "./draw-state"
import { buildDrawControl } from "./draw-control"
import { enableCrosshair, disableSnapping, enableSnapping } from "../snapping/snapping"
import { isEditMode } from "../edit/edit-mode"

// ─── REGISTRATION ─────────────────────────────────────────────────────

export function registerDrawEvents(): void {
  setRepatchMarkerPointer(repatchMarkerPointer)
  watchDrawType()
  registerDrawHandlers()
  patchGeomanMarkerPointerSnap()
  installSnapInterceptors()
}

// ─── FIX #4: REACTIVE DRAW TYPE ───────────────────────────────────────

function watchDrawType() {
  const appStore = useAppStore()
  watch(
    () => appStore.currentPhase,
    (phaseIdx) => {
      const activeDrawModes = ctx.geoman?.getActiveDrawModes?.() || []
      if (activeDrawModes.length > 0) {
        debugWarn("[WATCH] Phase changed while draw mode is active; forcing mode sync")
      }

      const phase = PHASES[phaseIdx]
      if (phase) {
        setDrawingPhase(phase)
        buildDrawControl(phase)

        if (phase.key === "cityCenter") {
          requestAnimationFrame(() => {
            const userLat = appStore.user?.commune?.latitude
            const userLng = appStore.user?.commune?.longitude

            if (userLat && userLng) {
              ctx.map.flyTo({
                center: [userLng, userLat],
                zoom: 16,
                duration: 1500,
                essential: true,
              })
            } else if (appStore.cityCenterLatLng) {
              ctx.map.flyTo({
                center: [appStore.cityCenterLatLng.lng, appStore.cityCenterLatLng.lat],
                zoom: 17,
                duration: 1500,
                essential: true,
              })
            }
          })
        }
      }

      if (!isEditMode()) {
        enableCrosshair()
        disableSnapping()
        enableSnapping()
      }
    },
    { immediate: true },
  )
}
