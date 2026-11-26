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

import { getCtx } from "../core/state"
import { useDrawStore } from "../../stores/drawStore"
import { setRepatchMarkerPointer } from "./draw-state"
import { repatchMarkerPointer } from "./draw-marker-patch"
import { registerDrawHandlers, destroyDrawHandlers } from "./draw-handlers"
import { patchGeomanMarkerPointerSnap } from "./draw-marker-patch"
import { installSnapInterceptors, uninstallSnapInterceptors } from "../snapping/snapping"
import { setDrawingPhase } from "./draw-state"
import { buildDrawControl, cancelPendingDrawModeSwitch } from "./draw-control"
import { disableCrosshair, resetSnapping, disableSnapping } from "../snapping/snapping"
import { isEditMode } from "../edit/edit-mode"

export function registerDrawEvents(): void {
  setRepatchMarkerPointer(repatchMarkerPointer)
  watchDrawType()
  registerDrawHandlers()
  patchGeomanMarkerPointerSnap()
  installSnapInterceptors()
}

export function destroyDrawEvents(): void {
  const store = useDrawStore()
  store.cleanupDrawWatcher?.()
  store.cleanupDrawWatcher = null
  destroyDrawHandlers()
  disableSnapping()
  uninstallSnapInterceptors()
}

function watchDrawType() {
  const appStore = useAppStore()
  const store = useDrawStore()

  // Stop the previous watcher before creating a new one — registerDrawEvents()
  // may be called again (HMR, re-init, tests) without destroyDrawEvents() in
  // between, which would otherwise accumulate leaking watchers.
  store.cleanupDrawWatcher?.()
  store.cleanupDrawWatcher = null

  store.cleanupDrawWatcher = watch(
    () => appStore.currentPhase,
    (phaseIdx) => {
      // A previous phase may still have a delayed enableDraw() pending. Make
      // sure it cannot arm its outdated shape after this phase change.
      cancelPendingDrawModeSwitch()

      const activeDrawModes = getCtx().geoman?.getActiveDrawModes?.() || []
      const drawingActive = activeDrawModes.length > 0
      if (drawingActive) {
        debugWarn("[WATCH] Phase changed while draw mode is active; syncing draw tool")
      }

      const phase = PHASES[phaseIdx]
      if (phase) {
        setDrawingPhase(phase)
        // Hand (pan/select) is the default mode: a phase change does not arm
        // drawing on its own. But if the user already had a draw tool active,
        // it must follow the new phase — otherwise the previous phase's shape
        // (polygon/line/marker/circle) stays armed and every new feature is
        // drawn with the wrong method.
        if (drawingActive && !isEditMode()) {
          void buildDrawControl(phase)
        }

        if (phase.key === "cityCenter") {
          requestAnimationFrame(() => {
            const userLat = appStore.user?.commune?.latitude
            const userLng = appStore.user?.commune?.longitude

            if (userLat && userLng) {
              getCtx().map.flyTo({
                center: [userLng, userLat],
                zoom: 16,
                duration: 1500,
                essential: true,
              })
            } else if (appStore.cityCenterLatLng) {
              getCtx().map.flyTo({
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
        // Keep the crosshair when a draw tool remains active; only clear it
        // when the phase switch leaves the user in hand (pan/select) mode.
        if (!drawingActive) disableCrosshair()
        resetSnapping()
      }
    },
    { immediate: true },
  )
}
