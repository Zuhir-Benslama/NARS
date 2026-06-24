import { PHASES } from "../../phases"
import { useDrawStore } from "../../stores/drawStore"
import type { GeomanMarkerPointer } from "../core/geoman-types"

type LngLatInput = [number, number] | { lng: number; lat: number }
type SetLngLatFn = (lngLat: LngLatInput) => void

export function registerGeomanMarker(
  mp: GeomanMarkerPointer,
  _marker: unknown,
  orig: SetLngLatFn,
): void {
  const store = useDrawStore()
  store.geomanMarkerPointer = mp
  store.originalGeomanMarkerSetLngLat = orig
}

export function unpatchGeomanMarker(): void {
  const store = useDrawStore()
  store.snappingEnabled = false
  if (store.geomanMarkerPointer?.marker && store.originalGeomanMarkerSetLngLat) {
    store.geomanMarkerPointer.marker.setLngLat = store.originalGeomanMarkerSetLngLat
    store.geomanMarkerPointer.marker._narsSnapPatchedInstance = false
  }
}

export function isSnappingEnabled(): boolean {
  return useDrawStore().snappingEnabled
}

export function setSnappingEnabled(v: boolean): void {
  useDrawStore().setSnappingEnabled(v)
}

export function setRepatchMarkerPointer(fn: () => void): void {
  useDrawStore().setRepatchMarkerPointer(fn)
}

export function repatchMarker(): void {
  useDrawStore().repatchMarker()
}

export function setDrawingPhase(phase: (typeof PHASES)[number] | null): void {
  useDrawStore().setDrawingPhase(phase)
}

export function getDrawingPhase(): (typeof PHASES)[number] | null {
  return useDrawStore().drawingPhase
}

export function isSavingFeature(): boolean {
  return useDrawStore().savingFeature
}

export function setSavingFeature(v: boolean): void {
  useDrawStore().setSavingFeature(v)
}

export function resetDrawState(): void {
  useDrawStore().resetDraw()
}
