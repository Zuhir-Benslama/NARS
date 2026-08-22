// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────

import { applyInitialLang } from "../i18n"
import { tryGetCtx } from "./core/state"
import { initRotationControls, destroyRotationControls } from "./rotation"
import { registerDrawEvents, destroyDrawEvents } from "./draw/draw-events"
import { registerGeomanEvents, unregisterGeomanEvents } from "./core/geoman-events"
import { disposeGeoman, initMap as initMapInstance } from "./map-init"
import { registerFieldWorkerClick, unregisterFieldWorkerClick } from "./field-click"
import { removeBoundaryClickEvents } from "./map-boundary"

// ─── RE-EXPORTS ───────────────────────────────────────────────────────

export { setBaseLayer } from "./map-init"
export { displayCommuneBoundary } from "./rendering/geometry"
export { bindContextMenu } from "./context-menu/context-menu"
export { buildFeatureData, toApiSaveShape } from "./features/feature-data"
export { saveToDatabase } from "./features/feature-persistence"
export { prepareModalExtras } from "./features/feature-modal"
export { areaStyle } from "./rendering/styles"
export { loadFromDatabase, loadUserAndCommune } from "./features/loader"
export { navigatePhase, goToPhase, setPhase } from "../phases-nav/navigation"
export { setHouseNumbers, getFeatureType } from "./house-numbering"

// ─── FULL INITIALIZATION / CLEANUP ────────────────────────────────────────────

export async function initMap(): Promise<void> {
  await initMapInstance()
  initRotationControls()
  await applyInitialLang()
  registerDrawEvents()
  registerGeomanEvents()
  registerFieldWorkerClick()
}

export async function destroyMap(): Promise<void> {
  const ctx = tryGetCtx()
  if (!ctx) return
  destroyRotationControls()
  destroyDrawEvents()
  unregisterGeomanEvents()
  unregisterFieldWorkerClick()
  removeBoundaryClickEvents()
  await disposeGeoman()
  ctx.map.remove()
}
