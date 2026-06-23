// ─── MAP ORCHESTRATOR ─────────────────────────────────────────────────────────

import { applyInitialLang } from "../i18n"
import { initRotationControls } from "./rotation"
import { registerDrawEvents } from "./draw/draw-events"
import { registerGeomanEvents } from "./core/geoman-events"
import { initMap as initMapInstance } from "./map-init"
import { registerFieldWorkerClick } from "./field-click"
import { destroyDrawEvents } from "./draw/draw-events"

// ─── RE-EXPORTS ───────────────────────────────────────────────────────

export { setBaseLayer } from "./map-init"
export { displayCommuneBoundary } from "./rendering/geometry"
export { bindContextMenu } from "./context-menu/context-menu"
export { buildFeatureData, toApiSaveShape } from "./features/feature-data"
export { saveToDatabase } from "./features/feature-persistence"
export { fetchRoadSide, computeBisNumber, prepareModalExtras } from "./features/feature-modal"
export { createEntranceIconHtml, areaStyle } from "./rendering/styles"
export { loadFromDatabase, loadUserAndCommune } from "./features/loader"
export { navigatePhase, goToPhase, setPhase } from "../phases-nav/navigation"
export { setHouseNumbers, getFeatureType } from "./house-numbering"
export { updateDrawingPreview } from "./map-layers"

// ─── FULL INITIALIZATION / CLEANUP ────────────────────────────────────────────

export async function initMap(): Promise<void> {
  await initMapInstance()
  initRotationControls()
  await applyInitialLang()
  registerDrawEvents()
  registerGeomanEvents()
  registerFieldWorkerClick()
}

export function destroyMap(): void {
  destroyDrawEvents()
}
