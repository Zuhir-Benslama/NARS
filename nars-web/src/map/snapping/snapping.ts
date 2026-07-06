import maplibregl from "maplibre-gl"
import { ctx } from "../core/state"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore } from "../../stores/layerStore"
import { useSnapStore } from "../../stores/snapStore"
import type { LayerState } from "../../stores/layerStore"
import { SNAP_CONFIG } from "../../config"
import { PHASES } from "../../phases"
import {
  unpatchGeomanMarker,
  repatchMarker,
  isSnappingEnabled,
  setSnappingEnabled,
} from "../draw/draw-complete"
import { findNearestSnap } from "./snap-search"
export { findNearestSnap, mergeExternalSnapWithDrawFirstVertex } from "./snap-search"
export type { SnapResult } from "./snap-search"

import { setSnapSourceExclude } from "./snap-sources"
export { getSnapRings, getRoadChains, getCityCenterCircles, getSnapPoints } from "./snap-sources"

declare global {
  interface Window {
    __narsSnapLatLng?: { lat: number; lng: number } | null
  }
}

let snapMarker: maplibregl.Marker | null = null
let snapCursor: HTMLDivElement | null = null
if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    snapMarker?.remove()
    snapCursor?.remove()
    snapMarker = null
    snapCursor = null
  })
}

export function resetSnapState(): void {
  const store = useSnapStore()
  store.resetSnap()
  snapMarker = null
  snapCursor = null
}

export function isSnapFrozen(): boolean {
  return useSnapStore().snapFrozen
}

export function getFrozenSnapPos(): { lat: number; lng: number } | null {
  return useSnapStore().getFrozenSnapPos
}

export function setEditModeActive(v: boolean): void {
  useSnapStore().setEditModeActive(v)
}

export function setEditDragActive(v: boolean): void {
  useSnapStore().setEditDragActive(v)
}

export function setSnapExclude(id: string | null): void {
  setSnapSourceExclude(id)
}

export function getActiveSnapPhases(): string[] {
  const store = useSnapStore()
  if (store.editModeActive) {
    const layerStore = useLayerStore()
    const state = layerStore.$state
    return Object.keys(state).filter((key) => {
      const entries = state[key as keyof LayerState]
      return entries && entries.length > 0
    })
  }

  const currentPhaseKey = PHASES[useAppStore().currentPhase]?.key ?? ""
  const allowedTargets =
    SNAP_CONFIG.snapTargets[currentPhaseKey as keyof typeof SNAP_CONFIG.snapTargets] ?? []

  const completedPhaseKeys = new Set<string>()
  for (let i = 0; i <= useAppStore().currentPhase; i++) {
    completedPhaseKeys.add(PHASES[i].key)
  }

  return [...allowedTargets].filter((key) => completedPhaseKeys.has(key))
}

export function enableCrosshair(): void {
  const store = useSnapStore()
  if (store.crosshairActive) return
  store.crosshairActive = true
  ctx.map.getCanvas().style.cursor = "crosshair"
}

export function disableCrosshair(): void {
  const store = useSnapStore()
  if (!store.crosshairActive) return
  store.crosshairActive = false
  ctx.map.getCanvas().style.cursor = ""
}

function onMouseDown(): void {
  const store = useSnapStore()
  if (!store.editModeActive && store.snapActive && store.snapLatLng) store.snapFrozen = true
}

function onMouseUp(): void {
  useSnapStore().snapFrozen = false
}

export function enableSnapping(): void {
  if (isSnappingEnabled()) return
  const store = useSnapStore()
  setSnappingEnabled(true)
  store.snapActive = true
  setSnapSourceExclude(null)
  ctx.map.getContainer().addEventListener("mousemove", onSnapMove, true)
  ctx.map.getContainer().addEventListener("mousedown", onMouseDown, true)
  ctx.map.getContainer().addEventListener("mouseup", onMouseUp, true)
  repatchMarker()
}

export function disableSnapping(): void {
  if (!isSnappingEnabled()) return
  const store = useSnapStore()
  setSnappingEnabled(false)
  store.snapActive = false
  setSnapSourceExclude(null)
  store.snapFrozen = false
  store.editModeActive = false
  if (store.snapRafId !== null) {
    cancelAnimationFrame(store.snapRafId)
    store.snapRafId = null
  }
  store.snapPendingEvent = null
  ctx.map.getContainer().removeEventListener("mousemove", onSnapMove, true)
  ctx.map.getContainer().removeEventListener("mousedown", onMouseDown, true)
  ctx.map.getContainer().removeEventListener("mouseup", onMouseUp, true)
  if (snapMarker) {
    snapMarker.remove()
    snapMarker = null
  }
  if (snapCursor) {
    snapCursor.remove()
    snapCursor = null
  }
  if (import.meta.env.DEV) {
    window.__narsSnapLatLng = null
  }
  ctx.map.getCanvas().style.cursor = store.crosshairActive ? "crosshair" : ""
  unpatchGeomanMarker()
}

export function toggleSnapping(): boolean {
  if (isSnappingEnabled()) {
    disableSnapping()
    return false
  } else {
    enableSnapping()
    return true
  }
}

export function resetSnapping(): void {
  disableSnapping()
  enableSnapping()
}

export function isSnappingActive(): boolean {
  return useSnapStore().snapActive
}

function onSnapMove(e: MouseEvent): void {
  const store = useSnapStore()
  store.snapPendingEvent = e
  if (store.snapRafId !== null) return
  store.snapRafId = requestAnimationFrame(processSnapMove)
}

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    const store = useSnapStore()
    if (store.snapRafId !== null) {
      cancelAnimationFrame(store.snapRafId)
      store.snapRafId = null
    }
  })
}

function processSnapMove(): void {
  const store = useSnapStore()
  store.snapRafId = null
  const e = store.snapPendingEvent
  store.snapPendingEvent = null
  if (!e || store.snapFrozen) return

  if (!ctx.map.getContainer().contains(e.target as Node)) return

  if (store.editModeActive && !store.editDragActive) {
    if (store.snapActive) {
      store.snapActive = false
      store.snapLatLng = null
      snapMarker?.remove()
      snapMarker = null
      snapCursor?.remove()
      snapCursor = null
    }
    return
  }

  const activePhases = getActiveSnapPhases()
  if (activePhases.length === 0) return

  const rect = ctx.map.getContainer().getBoundingClientRect()
  const x = e.clientX - rect.left
  const y = e.clientY - rect.top

  const snap = findNearestSnap(x, y, activePhases, true)
  if (snap) {
    store.snapActive = true
    store.snapLatLng = { lat: snap.lat, lng: snap.lng }
    const pos = ctx.map.project([snap.lng, snap.lat])
    showSnapIndicator(pos.x, pos.y, snap.type)
    if (import.meta.env.DEV) window.__narsSnapLatLng = store.snapLatLng
  } else {
    store.snapActive = false
    store.snapLatLng = null
    if (import.meta.env.DEV) window.__narsSnapLatLng = null
    snapMarker?.remove()
    snapMarker = null
    snapCursor?.remove()
    snapCursor = null
    ctx.map.getCanvas().style.cursor = store.crosshairActive ? "crosshair" : ""
  }
}

const SNAP_COLORS: Record<string, string> = {
  vertex: "#f39c12",
  midpoint: "#f39c12",
  edge: "#27ae60",
  circle: "#e74c3c",
}

const MARKER_SHAPES: Record<string, string> = {
  vertex:
    "width:16px;height:16px;background:yellow;border:3px solid {color};border-radius:50%;box-shadow:0 0 8px rgba(0,0,0,0.5);",
  midpoint:
    "width:12px;height:12px;background:{color};border:2px solid white;border-radius:2px;transform:translate(-50%,-50%) rotate(45deg);box-shadow:0 0 6px rgba(0,0,0,0.5);",
  circle:
    "width:10px;height:10px;background:{color};border:2px solid white;border-radius:50%;box-shadow:0 0 6px rgba(0,0,0,0.5);",
  edge: "width:12px;height:12px;background:transparent;border:2px solid {color};border-radius:2px;box-shadow:0 0 6px rgba(0,0,0,0.4);",
}

function showSnapIndicator(px: number, py: number, type: string): void {
  const color = SNAP_COLORS[type]
  const snapLngLat = useSnapStore().snapLatLng
  if (!snapMarker) {
    const el = document.createElement("div")
    snapMarker = new maplibregl.Marker({ element: el, anchor: "center", pitchAlignment: "map" })
    snapMarker.setLngLat([snapLngLat?.lng ?? 0, snapLngLat?.lat ?? 0]).addTo(ctx.map)
  }
  if (snapLngLat) {
    snapMarker.setLngLat([snapLngLat.lng, snapLngLat.lat])
    const el = snapMarker.getElement()
    el.style.cssText = MARKER_SHAPES[type].replace("{color}", color)
  }

  ctx.map.getCanvas().style.cursor = "crosshair"
  if (!snapCursor) {
    snapCursor = document.createElement("div")
    snapCursor.className = "nars-snap-crosshair"
    ctx.map.getContainer().appendChild(snapCursor)
  }
  snapCursor.style.cssText = `
    position:absolute;pointer-events:none;z-index:10000;
    left:${px}px;top:${py}px;
    --snap-color:${color};
  `
}

export function snapPointForEdit(
  cursorX: number,
  cursorY: number,
  excludeId: string | null,
): { lat: number; lng: number } | null {
  const result = findNearestSnap(cursorX, cursorY, getActiveSnapPhases(), true, excludeId)
  return result ? { lat: result.lat, lng: result.lng } : null
}

export function installSnapInterceptors(): void {
  const map = ctx.map

  const snapLngLat = (e: Record<string, unknown>): void => {
    const store = useSnapStore()
    if (!store.snapActive || !store.snapLatLng) return
    const { lng, lat } = store.snapLatLng
    try {
      Object.defineProperty(e, "lngLat", {
        value: { lng, lat, toArray: () => [lng, lat] },
        writable: true,
        configurable: true,
      })
    } catch {
      // Property is non-configurable in this MapLibre version — skip safely.
    }
  }

  map.on("click", snapLngLat)
  map.on("mousedown", snapLngLat)
}
