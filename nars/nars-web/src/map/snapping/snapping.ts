// ─── SNAP STATE MACHINE ───────────────────────────────────────────────────────
// Snap priority (highest first): circle → vertex → midpoint → edge
// Only searches phases that are ≤ current phase (completed phases), not future ones.
//
// Matching the reference (nars-web/Leaflet) snapping.ts:
// - Snap freeze on mousedown (prevents cursor jump during click)
// - Vertex + edge snapping for polygons/roads
// - Circle perimeter snapping for city centers
// - Edit-mode vertex hooking (in-place ring mutation on dragend)
// - installSnapInterceptors patches map events for drawing-mode snap

import { ctx } from "../core/state"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore } from "../../stores/layerStore"
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

// ─── DEV WINDOW EXTENSION ─────────────────────────────────────────────────────

declare global {
  interface Window {
    __narsSnapLatLng?: { lat: number; lng: number } | null
  }
}

// ─── SNAP STATE ───────────────────────────────────────────────────────────────

let crosshairActive = false
let snapMarker: HTMLDivElement | null = null
let snapCursor: HTMLDivElement | null = null
let snapActive = false
let snapLatLng: { lat: number; lng: number } | null = null
let snapFrozen = false
let snapRafId: number | null = null
let snapPendingEvent: MouseEvent | null = null

// ─── STATE RESET (for testing & HMR) ──────────────────────────────────────────

export function resetSnapState(): void {
  crosshairActive = false
  snapMarker = null
  snapCursor = null
  snapActive = false
  snapLatLng = null
  snapFrozen = false
  editModeActive = false
  editDragActive = false
  snapRafId = null
  snapPendingEvent = null
}
export function isSnapFrozen(): boolean {
  return snapFrozen
}

export function getFrozenSnapPos(): { lat: number; lng: number } | null {
  return snapFrozen && snapLatLng ? { lat: snapLatLng.lat, lng: snapLatLng.lng } : null
}

export let editModeActive = false
export function setEditModeActive(v: boolean): void {
  editModeActive = v
}
export let editDragActive = false
export function setEditDragActive(v: boolean): void {
  editDragActive = v
}
export function setSnapExclude(id: string | null): void {
  setSnapSourceExclude(id)
}

export function getActiveSnapPhases(): string[] {
  if (editModeActive) {
    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
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

  return ([...allowedTargets] as string[]).filter((key) => completedPhaseKeys.has(key))
}

// ─── CROSSHAIR CURSOR ─────────────────────────────────────────────────────────

export function enableCrosshair(): void {
  if (crosshairActive) return
  crosshairActive = true
  ctx.map.getCanvas().style.cursor = "crosshair"
}

export function disableCrosshair(): void {
  if (!crosshairActive) return
  crosshairActive = false
  ctx.map.getCanvas().style.cursor = ""
}

// ─── SNAP LIFECYCLE ───────────────────────────────────────────────────────────

function onMouseDown(): void {
  if (!editModeActive && snapActive && snapLatLng) snapFrozen = true
}
function onMouseUp(): void {
  snapFrozen = false
}

export function enableSnapping(): void {
  if (isSnappingEnabled()) return
  setSnappingEnabled(true)
  snapActive = true
  setSnapSourceExclude(null)
  ctx.map.getContainer().addEventListener("mousemove", onSnapMove, true)
  ctx.map.getContainer().addEventListener("mousedown", onMouseDown, true)
  ctx.map.getContainer().addEventListener("mouseup", onMouseUp, true)
  repatchMarker()
}

export function disableSnapping(): void {
  if (!isSnappingEnabled()) return
  setSnappingEnabled(false)
  snapActive = false
  setSnapSourceExclude(null)
  snapFrozen = false
  editModeActive = false
  if (snapRafId !== null) {
    cancelAnimationFrame(snapRafId)
    snapRafId = null
  }
  snapPendingEvent = null
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
  if (import.meta.env.DEV) window.__narsSnapLatLng = null
  ctx.map.getCanvas().style.cursor = crosshairActive ? "crosshair" : ""
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

export function isSnappingActive(): boolean {
  return snapActive
}

// ─── SNAP EVENT ───────────────────────────────────────────────────────────────

function onSnapMove(e: MouseEvent): void {
  snapPendingEvent = e
  if (snapRafId !== null) return
  snapRafId = requestAnimationFrame(processSnapMove)
}

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    if (snapRafId !== null) {
      cancelAnimationFrame(snapRafId)
      snapRafId = null
    }
  })
}

function processSnapMove(): void {
  snapRafId = null
  const e = snapPendingEvent
  snapPendingEvent = null
  if (!e || snapFrozen) return

  if (!ctx.map.getContainer().contains(e.target as Node)) return

  if (editModeActive && !editDragActive) {
    if (snapActive) {
      snapActive = false
      snapLatLng = null
      if (snapMarker) {
        snapMarker.remove()
        snapMarker = null
      }
      if (snapCursor) {
        snapCursor.remove()
        snapCursor = null
      }
    }
    return
  }

  const activePhases = getActiveSnapPhases()
  if (activePhases.length === 0) return

  const rect = ctx.map.getContainer().getBoundingClientRect()
  const x = e.clientX - rect.left
  const y = e.clientY - rect.top

  const snap = findNearestSnap(x, y, activePhases, false)
  if (snap) {
    snapActive = true
    snapLatLng = { lat: snap.lat, lng: snap.lng }
    const pos = ctx.map.project([snap.lng, snap.lat])
    showSnapIndicator(pos.x, pos.y, snap.type)
    if (import.meta.env.DEV) window.__narsSnapLatLng = snapLatLng
  } else {
    snapActive = false
    snapLatLng = null
    if (import.meta.env.DEV) window.__narsSnapLatLng = null
    if (snapMarker) {
      snapMarker.remove()
      snapMarker = null
    }
    if (snapCursor) {
      snapCursor.remove()
      snapCursor = null
    }
    ctx.map.getCanvas().style.cursor = crosshairActive ? "crosshair" : ""
  }
}

// ─── SNAP INDICATOR STYLES ────────────────────────────────────────────────────

const SNAP_COLORS: Record<string, string> = {
  vertex: "#f39c12",
  midpoint: "#f39c12",
  edge: "#27ae60",
  circle: "#e74c3c",
}

const MARKER_STYLE: Record<string, string> = {
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
  if (!snapMarker) {
    snapMarker = document.createElement("div")
    ctx.map.getContainer().appendChild(snapMarker)
  }

  const position = `position:absolute;pointer-events:none;z-index:9998;transform:translate(-50%,-50%);left:${px}px;top:${py}px;`
  const shape = MARKER_STYLE[type].replace("{color}", color)
  snapMarker.style.cssText = position + shape

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

// ─── EDIT MODE SNAP ───────────────────────────────────────────────────────────

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
    if (!snapActive || !snapLatLng) return
    const { lng, lat } = snapLatLng
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
