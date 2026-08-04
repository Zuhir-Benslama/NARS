import { PHASES } from "../../phases"
import { useDrawStore } from "../../stores/drawStore"
import type { GeomanMarkerPointer } from "../core/geoman-types"
import type { SetLngLatFn } from "./types"
import { getNarsLastSnap } from "./draw-marker-patch"

export function registerGeomanMarker(
  mp: GeomanMarkerPointer,
  marker: unknown,
  orig: SetLngLatFn,
): void {
  useDrawStore().registerGeomanMarker(mp, marker, orig)
}

export function unpatchGeomanMarker(): void {
  useDrawStore().unpatchGeomanMarker()
}

/**
 * Expose the last snapped position for consumers that need it (e.g. editing
 * modes that read cursor position after the marker moves).
 */
export { getNarsLastSnap }

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
