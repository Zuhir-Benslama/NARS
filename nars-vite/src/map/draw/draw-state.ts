// ─── DRAW STATE ───────────────────────────────────────────────────────────────
// Module-level state shared across draw-complete, draw-events, and draw-marker-patch:
// - Geoman marker setLngLat bridge
// - Drawing phase tracker
// - Save-in-progress guard

import { PHASES } from "../../phases"

// ─── GEOMAN MARKER BRIDGE ─────────────────────────────────────────────────────

let _geomanMarkerPointer: Record<string, unknown> | null = null
let _originalGeomanMarkerSetLngLat: ((...args: unknown[]) => void) | null = null
let _snappingEnabled = true

export function registerGeomanMarker(
  mp: Record<string, unknown>,
  _marker: unknown,
  orig: (...args: unknown[]) => void,
): void {
  _geomanMarkerPointer = mp
  _originalGeomanMarkerSetLngLat = orig
}

export function unpatchGeomanMarker(): void {
  _snappingEnabled = false
  if (_geomanMarkerPointer?.marker && _originalGeomanMarkerSetLngLat) {
    const marker = _geomanMarkerPointer.marker as Record<string, unknown>
    marker.setLngLat = _originalGeomanMarkerSetLngLat
    marker._narsSnapPatchedInstance = false
  }
}

export function isSnappingEnabled(): boolean {
  return _snappingEnabled
}

export function setSnappingEnabled(v: boolean): void {
  _snappingEnabled = v
}

// ─── RE-PATCH BRIDGE ──────────────────────────────────────────────────────────

let _repatchMarkerPointer: (() => void) | null = null

export function setRepatchMarkerPointer(fn: () => void): void {
  _repatchMarkerPointer = fn
}

export function repatchMarker(): void {
  _repatchMarkerPointer?.()
}

// ─── DRAW PHASE ───────────────────────────────────────────────────────────────

let _drawingPhase: (typeof PHASES)[number] | null = null

export function setDrawingPhase(phase: (typeof PHASES)[number] | null): void {
  _drawingPhase = phase
}

export function getDrawingPhase(): (typeof PHASES)[number] | null {
  return _drawingPhase
}

// ─── SAVE GUARD ───────────────────────────────────────────────────────────────

let savingFeature = false

export function isSavingFeature(): boolean {
  return savingFeature
}

export function setSavingFeature(v: boolean): void {
  savingFeature = v
}
