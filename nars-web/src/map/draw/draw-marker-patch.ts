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

import { ctx } from "../core/state"
import { useDrawStore } from "../../stores/drawStore"
import { findNearestSnap, mergeExternalSnapWithDrawFirstVertex } from "../snapping/snap-search"
import { getFrozenSnapPos, getActiveSnapPhases } from "../snapping/snapping"
import { registerGeomanMarker } from "./draw-complete"
import { debugLog, debugWarn } from "../../utils/debug"
import type { GeomanMarkerPointer } from "../core/geoman-types"

// ─── SNAP SET-LNG-LAT FACTORY ─────────────────────────────────────────────────

type LngLatInput = [number, number] | { lng: number; lat: number; toArray?(): [number, number] }
type SetLngLatFn = (lngLat: LngLatInput) => void

export function makeSnapSetLngLat(mp: GeomanMarkerPointer, orig: SetLngLatFn): SetLngLatFn {
  const SNAP_KEY = "_narsLastSnap"
  return function (lngLat) {
    const rawPair = Array.isArray(lngLat)
      ? lngLat
      : (lngLat.toArray?.() ?? [lngLat.lng ?? 0, lngLat.lat ?? 0])
    const lng0 = Number(rawPair[0])
    const lat0 = Number(rawPair[1])
    const rawPx = ctx.map.project([lng0, lat0] as [number, number])

    const frozen = getFrozenSnapPos()
    if (frozen) {
      ;(mp.marker as Record<string, unknown>)[SNAP_KEY] = { lng: frozen.lng, lat: frozen.lat }
      orig.call(mp.marker!, [frozen.lng, frozen.lat])
      return
    }

    const phases = getActiveSnapPhases()
    const project = (ll: [number, number]) => ctx.map.project(ll)
    const external = phases.length > 0 ? findNearestSnap(rawPx.x, rawPx.y, phases, true) : null
    const snap = mergeExternalSnapWithDrawFirstVertex(rawPx.x, rawPx.y, external, project)
    if (snap) {
      ;(mp.marker as Record<string, unknown>)[SNAP_KEY] = { lng: snap.lng, lat: snap.lat }
      orig.call(mp.marker!, [snap.lng, snap.lat])
    } else {
      ;(mp.marker as Record<string, unknown>)[SNAP_KEY] = null
      orig.call(mp.marker!, lngLat)
    }
  }
}

// ─── SHARED PATCH LOGIC ───────────────────────────────────────────────────────

function applyMarkerPatch(mp: GeomanMarkerPointer): void {
  const orig = mp.marker!.setLngLat.bind(mp.marker)
  const origGet = mp.marker!.getLngLat.bind(mp.marker)
  registerGeomanMarker(mp, mp.marker!, orig)
  mp.marker!._narsSnapPatchedInstance = true
  const SNAP_KEY = "_narsLastSnap"
  ;(mp.marker as Record<string, unknown>)["_narsOrigGetLngLat"] = origGet
  mp.marker!.setLngLat = makeSnapSetLngLat(mp, orig)
  mp.marker!.getLngLat = () => {
    const snap = (mp.marker as Record<string, unknown>)[SNAP_KEY] as {
      lng: number
      lat: number
    } | null
    return snap ? [snap.lng, snap.lat] : origGet.call(mp.marker!)
  }

  const markerEl = (
    mp.marker as unknown as { markerInstance?: { getElement(): HTMLElement } }
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

// ─── PATCH REGISTRATION ───────────────────────────────────────────────────────

export function patchGeomanMarkerPointerSnap(): void {
  const gm = ctx.geoman
  if (!gm?.markerPointer) {
    debugWarn("[SNAP] No markerPointer")
    return
  }

  const mp = gm.markerPointer as GeomanMarkerPointer
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
  const gm = ctx.geoman
  if (!gm?.markerPointer) return
  const mp = gm.markerPointer as GeomanMarkerPointer
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
