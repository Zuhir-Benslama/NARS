// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────

import { applyInitialLang } from "../i18n"
import { initRotationControls } from "./rotation"
import { registerDrawEvents } from "./draw/draw-events"
import { registerGeomanEvents } from "./core/geoman-events"
import { initMap as initMapInstance } from "./map-init"
import { registerFieldWorkerClick } from "./field-click"

// ─── RE-EXPORTS ───────────────────────────────────────────────────────

export { setBaseLayer } from "./map-init"
export { displayCommuneBoundary } from "./rendering/geometry"
export { bindContextMenu } from "./context-menu/context-menu"
export { fetchRoadSide, computeBisNumber } from "./features/features"
export { createEntranceIconHtml, areaStyle } from "./rendering/styles"
export { loadFromDatabase, loadUserAndCommune } from "./features/loader"
export { navigatePhase, goToPhase, setPhase } from "./phases/phase-nav"
export { setHouseNumbers, getFeatureType } from "./house-numbering"
export { updateDrawingPreview } from "./map-layers"

// ─── FULL INITIALIZATION ──────────────────────────────────────────────────────

export async function initMap(): Promise<void> {
  await initMapInstance()
  initRotationControls()
  await applyInitialLang()
  registerDrawEvents()
  registerGeomanEvents()
  registerFieldWorkerClick()
}
