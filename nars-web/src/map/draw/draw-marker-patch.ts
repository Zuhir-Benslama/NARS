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

// ─── SNAP SET-LNG-LAT FACTORY ─────────────────────────────────────────────────

type LngLatInput = [number, number] | { lng: number; lat: number; toArray?(): [number, number] }
type SetLngLatFn = (lngLat: LngLatInput) => void

interface PatchedMarker {
  setLngLat: SetLngLatFn
  getLngLat: () => [number, number]
  _narsSnapPatchedInstance?: true
  _narsOrigGetLngLat?: () => [number, number]
  _narsLastSnap?: { lng: number; lat: number } | null
  markerInstance?: { getElement(): HTMLElement }
}

export function makeSnapSetLngLat(mp: GeomanMarkerPointer, orig: SetLngLatFn): SetLngLatFn {
  const marker = mp.marker as unknown as PatchedMarker
  return function (lngLat) {
    const rawPair = Array.isArray(lngLat)
      ? lngLat
      : (lngLat.toArray?.() ?? [lngLat.lng ?? 0, lngLat.lat ?? 0])
    const lng0 = Number(rawPair[0])
    const lat0 = Number(rawPair[1])
    const rawPx = getCtx().map.project([lng0, lat0])

    const frozen = getFrozenSnapPos()
    if (frozen) {
      marker._narsLastSnap = { lng: frozen.lng, lat: frozen.lat }
      orig.call(marker, [frozen.lng, frozen.lat])
      return
    }

    const phases = getActiveSnapPhases()
    const project = (ll: [number, number]) => getCtx().map.project(ll)
    const external = phases.length > 0 ? findNearestSnap(rawPx.x, rawPx.y, phases, true) : null
    const snap = mergeExternalSnapWithDrawFirstVertex(rawPx.x, rawPx.y, external, project)
    if (snap) {
      marker._narsLastSnap = { lng: snap.lng, lat: snap.lat }
      orig.call(marker, [snap.lng, snap.lat])
    } else {
      marker._narsLastSnap = null
      orig.call(marker, lngLat)
    }
  }
}

// ─── SHARED PATCH LOGIC ───────────────────────────────────────────────────────

function applyMarkerPatch(mp: GeomanMarkerPointer): void {
  const marker = mp.marker as unknown as PatchedMarker
  const orig = marker.setLngLat.bind(marker)
  const origGet = marker.getLngLat.bind(marker)
  registerGeomanMarker(mp, marker, orig)
  marker._narsSnapPatchedInstance = true
  marker._narsOrigGetLngLat = origGet
  marker.setLngLat = makeSnapSetLngLat(mp, orig)
  marker.getLngLat = () => {
    const snap = marker._narsLastSnap
    return snap ? [snap.lng, snap.lat] : origGet.call(marker)
  }

  const markerEl = marker.markerInstance?.getElement?.()
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
      if (mp.marker._narsSnapPatchedInstance) {
        if (rafRef.current !== null) cancelAnimationFrame(rafRef.current)
        rafRef.current = null
        return
      }

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
    () => debugLog("[SNAP] marker setLngLat + getLngLat patched"),
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
    const store = useDrawStore()
    if (store.patchRafRef.current !== null) {
      cancelAnimationFrame(store.patchRafRef.current)
      store.patchRafRef.current = null
    }
  })
}
