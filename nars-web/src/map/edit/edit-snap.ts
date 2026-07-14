// ─── EDIT SNAP PATCH ──────────────────────────────────────────────────────────
// Patches markerPointer.marker.setLngLat so every position update goes through
// our snap before being stored. A capture-phase mousemove does NOT work because
// markerPointer's own bubble-phase handler overwrites our position after we set it.

import type { LngLatTuple } from "@geoman-io/maplibre-geoman-free"
import { getCtx } from "../core/state"
import { useEditStore } from "../../stores/editStore"
import { snapPointForEdit } from "../snapping/snapping"

export function patchMarkerPointerSnap(editEntryId: string | null): void {
  const mp = getCtx().geoman?.markerPointer?.marker
  if (!mp) return
  const store = useEditStore()
  if (store.origSetLngLat) return

  store.origSetLngLat = mp.setLngLat.bind(mp)

  mp.setLngLat = (lngLat: LngLatTuple) => {
    const [lng, lat] = lngLat
    const px = getCtx().map.project([lng, lat])
    const snapped = snapPointForEdit(px.x, px.y, editEntryId ?? null)
    store.origSetLngLat!(snapped ? [snapped.lng, snapped.lat] : [lng, lat])
  }
}

export function unpatchMarkerPointerSnap(): void {
  const mp = getCtx().geoman?.markerPointer?.marker
  const store = useEditStore()
  if (mp && store.origSetLngLat) {
    mp.setLngLat = store.origSetLngLat
  }
  store.origSetLngLat = null
}

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    useEditStore().origSetLngLat = null
  })
}
