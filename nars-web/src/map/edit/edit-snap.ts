// ─── EDIT SNAP PATCH ──────────────────────────────────────────────────────────
// Patches markerPointer.marker.setLngLat so every position update goes through
// our snap before being stored. A capture-phase mousemove does NOT work because
// markerPointer's own bubble-phase handler overwrites our position after we set it.

import type { LngLatTuple } from "@geoman-io/maplibre-geoman-free"
import { ctx } from "../core/state"
import { snapPointForEdit } from "../snapping/snapping"

let _origSetLngLat: ((lngLat: LngLatTuple) => void) | null = null
if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    _origSetLngLat = null
  })
}

export function patchMarkerPointerSnap(editEntryId: string | null): void {
  const mp = ctx.geoman?.markerPointer?.marker
  if (!mp || _origSetLngLat) return

  _origSetLngLat = mp.setLngLat.bind(mp)

  mp.setLngLat = (lngLat: LngLatTuple) => {
    const [lng, lat] = lngLat
    const px = ctx.map.project([lng, lat])
    const snapped = snapPointForEdit(px.x, px.y, editEntryId ?? null)
    _origSetLngLat!(snapped ? [snapped.lng, snapped.lat] : [lng, lat])
  }
}

export function unpatchMarkerPointerSnap(): void {
  const mp = ctx.geoman?.markerPointer?.marker
  if (mp && _origSetLngLat) {
    mp.setLngLat = _origSetLngLat
  }
  _origSetLngLat = null
}
