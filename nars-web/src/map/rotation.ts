// ─── MAP ROTATION ─────────────────────────────────────────────────────────────
// Adds rotation controls to the Maplibre GL JS map.

import { ctx } from "./core/state"
import { useRotationStore } from "../stores/rotationStore"
import { t } from "../i18n"

const STEP = 5

export function resetRotation(): void {
  useRotationStore().resetRotation()
}

export function setBearing(deg: number): void {
  const store = useRotationStore()
  store.setBearing(deg)
  ctx.map.easeTo({ bearing: store.currentBearing, duration: 300 })
}

export function initRotationControls(): void {
  const container = ctx.map.getContainer()

  const wrap = document.createElement("div")
  wrap.className = "nars-rotation-control leaflet-bar"
  wrap.style.cssText = "position:absolute;bottom:10px;right:10px;display:flex;gap:4px;z-index:1000;"

  const ccw = document.createElement("button")
  ccw.textContent = "↺"
  ccw.title = t("rotate_ccw")
  ccw.className = "nars-map-btn"
  ccw.style.cssText = "width:30px;height:30px;cursor:pointer;"
  ccw.onclick = () => setBearing(useRotationStore().currentBearing - STEP)

  const cw = document.createElement("button")
  cw.textContent = "↻"
  cw.title = t("rotate_cw")
  cw.className = "nars-map-btn"
  cw.style.cssText = "width:30px;height:30px;cursor:pointer;"
  cw.onclick = () => setBearing(useRotationStore().currentBearing + STEP)

  wrap.appendChild(ccw)
  wrap.appendChild(cw)
  container.appendChild(wrap)
}
