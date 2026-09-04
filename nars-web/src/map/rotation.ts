// ─── MAP ROTATION ─────────────────────────────────────────────────────────────
// Adds rotation controls to the Maplibre GL JS map.

import { getCtx } from "./core/state"
import { useRotationStore } from "../stores/rotationStore"
import { t } from "../i18n"

const STEP = 5

let rotationControlEl: HTMLElement | null = null
let rotationStyleInjected = false

const ROTATION_STYLE = `
  .nars-rotation-control {
    position: absolute;
    bottom: 10px;
    right: 10px;
    display: flex;
    gap: 4px;
    z-index: 1000;
  }
  .nars-rotation-btn {
    width: 30px;
    height: 30px;
    cursor: pointer;
  }
`

function injectRotationStyle(): void {
  if (rotationStyleInjected) return
  const style = document.createElement("style")
  style.textContent = ROTATION_STYLE
  document.head.appendChild(style)
  rotationStyleInjected = true
}

export function resetRotation(): void {
  useRotationStore().resetRotation()
}

export function setBearing(deg: number): void {
  const store = useRotationStore()
  store.setBearing(deg)
  getCtx().map.easeTo({ bearing: store.currentBearing, duration: 300 })
}

export function initRotationControls(): void {
  injectRotationStyle()
  const container = getCtx().map.getContainer()

  const wrap = document.createElement("div")
  wrap.className = "nars-rotation-control"

  const ccw = document.createElement("button")
  ccw.textContent = "↺"
  ccw.title = t("rotate_ccw")
  ccw.setAttribute("aria-label", t("rotate_ccw"))
  ccw.className = "nars-map-btn nars-rotation-btn"
  ccw.onclick = () => setBearing(useRotationStore().currentBearing - STEP)

  const cw = document.createElement("button")
  cw.textContent = "↻"
  cw.title = t("rotate_cw")
  cw.setAttribute("aria-label", t("rotate_cw"))
  cw.className = "nars-map-btn nars-rotation-btn"
  cw.onclick = () => setBearing(useRotationStore().currentBearing + STEP)

  wrap.appendChild(ccw)
  wrap.appendChild(cw)
  container.appendChild(wrap)

  // Keep a reference so destroyMap() can remove the controls on teardown.
  // map.remove() does NOT remove these — they are appended to the container,
  // not registered as map controls.
  if (rotationControlEl) rotationControlEl.remove()
  rotationControlEl = wrap
}

export function destroyRotationControls(): void {
  if (rotationControlEl) {
    rotationControlEl.remove()
    rotationControlEl = null
  }
}
