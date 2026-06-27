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

  const PATCH_TIMEOUT_MS = 15_000
  const startTime = performance.now()
  let rafId: number | null = null

  const tryPatch = () => {
    if (mp.marker && typeof mp.marker.setLngLat === "function") {
      if (mp.marker._narsSnapPatchedInstance) {
        if (rafId !== null) cancelAnimationFrame(rafId)
        rafId = null
        return
      }

      const orig = mp.marker.setLngLat.bind(mp.marker)
      const origGet = mp.marker.getLngLat.bind(mp.marker)
      registerGeomanMarker(mp, mp.marker, orig)
      mp.marker._narsSnapPatchedInstance = true
      const SNAP_KEY = "_narsLastSnap"
      ;(mp.marker as Record<string, unknown>)["_narsOrigGetLngLat"] = origGet
      mp.marker.setLngLat = makeSnapSetLngLat(mp, orig)
      mp.marker.getLngLat = () => {
        const snap = (mp.marker as Record<string, unknown>)[SNAP_KEY] as {
          lng: number
          lat: number
        } | null
        return snap ?? origGet.call(mp.marker!)
      }

      debugLog("[SNAP] marker setLngLat + getLngLat patched")
      if (rafId !== null) cancelAnimationFrame(rafId)
      rafId = null
      return
    }

    if (performance.now() - startTime > PATCH_TIMEOUT_MS) {
      debugWarn("[SNAP] Timed out waiting for Geoman marker — snapping disabled")
      if (rafId !== null) cancelAnimationFrame(rafId)
      rafId = null
      return
    }

    rafId = requestAnimationFrame(tryPatch)
  }

  rafId = requestAnimationFrame(tryPatch)

  debugLog("[SNAP] Snap patching started (rAF polling for marker)")
}

// ─── RE-PATCH MARKER AFTER DRAW RESET ─────────────────────────────────────────

let _patchRafId: number | null = null

export function repatchMarkerPointer(): void {
  const gm = ctx.geoman
  if (!gm?.markerPointer) return
  const mp = gm.markerPointer as GeomanMarkerPointer
  if (!mp) return

  if (_patchRafId !== null) {
    cancelAnimationFrame(_patchRafId)
    _patchRafId = null
  }

  const PATCH_TIMEOUT_MS = 5_000
  const startTime = performance.now()

  const tryPatch = () => {
    if (mp.marker && typeof mp.marker.setLngLat === "function") {
      if (mp.marker._narsSnapPatchedInstance) return

      const orig = mp.marker.setLngLat.bind(mp.marker)
      const origGet = mp.marker.getLngLat.bind(mp.marker)
      registerGeomanMarker(mp, mp.marker, orig)
      mp.marker._narsSnapPatchedInstance = true
      const SNAP_KEY = "_narsLastSnap"
      ;(mp.marker as Record<string, unknown>)["_narsOrigGetLngLat"] = origGet
      mp.marker.setLngLat = makeSnapSetLngLat(mp, orig)
      mp.marker.getLngLat = () => {
        const snap = (mp.marker as Record<string, unknown>)[SNAP_KEY] as {
          lng: number
          lat: number
        } | null
        return snap ?? origGet.call(mp.marker!)
      }

      debugLog("[SNAP] marker re-patched after draw reset")
      _patchRafId = null
      return
    }

    if (performance.now() - startTime > PATCH_TIMEOUT_MS) {
      debugWarn("[SNAP] Timed out waiting for marker after draw reset")
      _patchRafId = null
      return
    }

    _patchRafId = requestAnimationFrame(tryPatch)
  }

  _patchRafId = requestAnimationFrame(tryPatch)
}

// ─── HMR CLEANUP ─────────────────────────────────────────────────────────────

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    if (_patchRafId !== null) {
      cancelAnimationFrame(_patchRafId)
      _patchRafId = null
    }
  })
}
