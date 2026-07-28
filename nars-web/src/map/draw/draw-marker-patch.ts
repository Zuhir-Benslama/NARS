// ─── GEOMAN MARKER POINTER PATCH ──────────────────────────────────────────────
// Geoman's MarkerPointer.onMouseMove is the SINGLE entry point for cursor
// positioning during ALL drawing modes (polygon, line, circle, marker, etc).
//
// Flow:
//   1. MapLibre fires mousemove event
//   2. MarkerPointer.onMouseMove(e) is called (throttled)
//   3. It calls this.snappingHelper.getSnappedLngLat() IF snapping is on
//   4. It calls this.marker.setLngLat(snappedCoords)
//   5. All draw classes read marker.getLngLat() for vertex placement
//
// Problem: The snapping helper action instance (helper__snapping) is only
// active during explicit snapping helper mode, not during regular drawing.
// So getSnappedLngLat returns the raw coordinates unchanged.
//
// Solution: Monkey-patch MarkerPointer.onMouseMove to use our NARS snap logic
// directly, bypassing the snapping helper entirely.

import { getCtx } from "../core/state"
import { useDrawStore } from "../../stores/drawStore"
import { findNearestSnap, mergeExternalSnapWithDrawFirstVertex } from "../snapping/snap-search"
import { getFrozenSnapPos, getActiveSnapPhases } from "../snapping/snapping"
import { registerGeomanMarker } from "./draw-complete"
import { debugLog, debugWarn } from "../../utils/debug"
import type { GeomanMarkerPointer } from "../core/geoman-types"

import type { SetLngLatFn } from "./types"

// ─── MODULE-LEVEL SNAP STATE ───────────────────────────────────────────────────
// Stored here instead of on the marker object so we don't pollute MapLibre
// marker instances with NARS-internal properties.

let narsLastSnap: { lng: number; lat: number } | null = null

export function getNarsLastSnap(): { lng: number; lat: number } | null {
  return narsLastSnap
}

// ─── SNAP SET-LNG-LAT FACTORY ─────────────────────────────────────────────────

export function makeSnapSetLngLat(mp: GeomanMarkerPointer, orig: SetLngLatFn): SetLngLatFn {
  return function (lngLat) {
    const rawPair = Array.isArray(lngLat)
      ? lngLat
      : (lngLat.toArray?.() ?? [lngLat.lng ?? 0, lngLat.lat ?? 0])
    const lng0 = Number(rawPair[0])
    const lat0 = Number(rawPair[1])
    const rawPx = getCtx().map.project([lng0, lat0])

    const frozen = getFrozenSnapPos()
    if (frozen) {
      narsLastSnap = { lng: frozen.lng, lat: frozen.lat }
      orig.call(mp.marker, [frozen.lng, frozen.lat])
      return
    }

    const phases = getActiveSnapPhases()
    const project = (ll: [number, number]) => getCtx().map.project(ll)
    const external = phases.length > 0 ? findNearestSnap(rawPx.x, rawPx.y, phases, true) : null
    const snap = mergeExternalSnapWithDrawFirstVertex(rawPx.x, rawPx.y, external, project)
    if (snap) {
      narsLastSnap = { lng: snap.lng, lat: snap.lat }
      orig.call(mp.marker, [snap.lng, snap.lat])
    } else {
      narsLastSnap = null
      orig.call(mp.marker, lngLat)
    }
  }
}

// ─── PATCH LOGIC ──────────────────────────────────────────────────────────────
// Patches marker.setLngLat to intercept cursor positions and apply NARS snapping.
// The original getLngLat is not patched — since we always call the original
// setLngLat with the snapped coordinates, the marker's internal position is
// already correct and getLngLat returns the right value naturally.

function applyMarkerPatch(mp: GeomanMarkerPointer): void {
  const marker = mp.marker as Record<string, unknown>
  const orig = marker.setLngLat as SetLngLatFn
  registerGeomanMarker(mp, marker, orig)
  marker.setLngLat = makeSnapSetLngLat(mp, orig)

  const markerEl = (
    marker as { markerInstance?: { getElement?(): HTMLElement } }
  ).markerInstance?.getElement?.()
  if (markerEl) {
    markerEl.style.pointerEvents = "none"
  }
}

type RafRef = { current: number | null }

function startPolling(
  mp: GeomanMarkerPointer,
  timeoutMs: number,
  rafRef: RafRef,
  onPatched: () => void,
  onTimeout: () => void,
): void {
  const startTime = performance.now()

  const tryPatch = () => {
    if (mp.marker && typeof mp.marker.setLngLat === "function") {
      applyMarkerPatch(mp)
      onPatched()
      if (rafRef.current !== null) cancelAnimationFrame(rafRef.current)
      rafRef.current = null
      return
    }

    if (performance.now() - startTime > timeoutMs) {
      onTimeout()
      if (rafRef.current !== null) cancelAnimationFrame(rafRef.current)
      rafRef.current = null
      return
    }

    rafRef.current = requestAnimationFrame(tryPatch)
  }

  rafRef.current = requestAnimationFrame(tryPatch)
}

// ─── SHARED HELPERS ───────────────────────────────────────────────────────────

function getMarkerPointer(): GeomanMarkerPointer | null {
  return (getCtx().geoman?.markerPointer as GeomanMarkerPointer | undefined) ?? null
}

// ─── PATCH REGISTRATION ───────────────────────────────────────────────────────

export function patchGeomanMarkerPointerSnap(): void {
  const mp = getMarkerPointer()
  if (!mp) {
    debugWarn("[SNAP] No markerPointer")
    return
  }
  if (mp._narsSnapPatched) return
  mp._narsSnapPatched = true

  const rafRef: RafRef = { current: null }
  startPolling(
    mp,
    15_000,
    rafRef,
    () => debugLog("[SNAP] marker setLngLat patched"),
    () => debugWarn("[SNAP] Timed out waiting for Geoman marker — snapping disabled"),
  )
  debugLog("[SNAP] Snap patching started (rAF polling for marker)")
}

// ─── RE-PATCH MARKER AFTER DRAW RESET ─────────────────────────────────────────

export function repatchMarkerPointer(): void {
  const mp = getMarkerPointer()
  if (!mp) return
  const store = useDrawStore()

  if (store.patchRafRef.current !== null) {
    cancelAnimationFrame(store.patchRafRef.current)
    store.patchRafRef.current = null
  }

  startPolling(
    mp,
    5_000,
    store.patchRafRef,
    () => debugLog("[SNAP] marker re-patched after draw reset"),
    () => debugWarn("[SNAP] Timed out waiting for marker after draw reset"),
  )
}

// ─── HMR CLEANUP ─────────────────────────────────────────────────────────────

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    narsLastSnap = null
    const store = useDrawStore()
    if (store.patchRafRef.current !== null) {
      cancelAnimationFrame(store.patchRafRef.current)
      store.patchRafRef.current = null
    }
  })
}
