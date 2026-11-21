// ─── EDIT SNAP PATCH ──────────────────────────────────────────────────────────
// Patches markerPointer.marker.setLngLat so every position update goes through
// our snap before being stored. A capture-phase mousemove does NOT work because
// markerPointer's own bubble-phase handler overwrites our position after we set it.

import type { LngLatTuple } from "@geoman-io/maplibre-geoman-free"
import { getCtx } from "../core/state"
import { useSnapStore } from "../../stores/snapStore"
import { snapPointForEdit } from "../snapping/snapping"

export function patchMarkerPointerSnap(editEntryId: string | null): void {
  const mp = getCtx().geoman?.markerPointer?.marker as
    | ({
        setLngLat: (lngLat: LngLatTuple) => unknown
        __origSetLngLat?: (lngLat: LngLatTuple) => unknown
      } & object)
    | undefined
  if (!mp) return

  // If this marker already carries its own original, it is already patched:
  // just update the closure's editEntryId by re-patching with the stored
  // original. Otherwise (or on a fresh marker) capture the current (unpatched)
  // implementation as the original.
  const existingOrig = (mp as { __origSetLngLat?: (lngLat: LngLatTuple) => unknown })
    .__origSetLngLat
  const orig = existingOrig ?? (mp.setLngLat as (lngLat: LngLatTuple) => unknown).bind(mp)

  // Restore original first to avoid compounding wrappers.
  mp.setLngLat = orig as (lngLat: LngLatTuple) => unknown

  ;(mp as { __origSetLngLat?: (lngLat: LngLatTuple) => unknown }).__origSetLngLat = orig

  const store = useSnapStore()
  store.setOrigMarkerSetLngLat(orig as (lngLat: LngLatTuple) => unknown)

  mp.setLngLat = (lngLat: LngLatTuple) => {
    const [lng, lat] = lngLat
    if (!useSnapStore().snappingEnabled) {
      orig([lng, lat])
      return
    }
    const px = getCtx().map.project([lng, lat])
    const snapped = snapPointForEdit(px.x, px.y, editEntryId ?? null)
    orig(snapped ? [snapped.lng, snapped.lat] : [lng, lat])
  }
}

export function unpatchMarkerPointerSnap(): void {
  const mp = getCtx().geoman?.markerPointer?.marker as
    | ({
        setLngLat: (lngLat: LngLatTuple) => unknown
        __origSetLngLat?: (lngLat: LngLatTuple) => unknown
      } & object)
    | undefined
  const store = useSnapStore()
  if (mp) {
    const orig = (mp as { __origSetLngLat?: (lngLat: LngLatTuple) => unknown }).__origSetLngLat
    if (orig) {
      mp.setLngLat = orig as (lngLat: LngLatTuple) => unknown
    }
    delete (mp as { __origSetLngLat?: (lngLat: LngLatTuple) => unknown }).__origSetLngLat
  }
  store.setOrigMarkerSetLngLat(null)
}

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    useSnapStore().setOrigMarkerSetLngLat(null)
  })
}
