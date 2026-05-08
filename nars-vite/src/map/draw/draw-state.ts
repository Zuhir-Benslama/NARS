// ─── DRAW STATE ───────────────────────────────────────────────────────────────
// Module-level state shared across draw-complete, draw-events, and draw-marker-patch:
// - Geoman marker setLngLat bridge
// - Drawing phase tracker
// - Save-in-progress guard

import { PHASES } from "../../phases"

// ─── GEOMAN MARKER BRIDGE ─────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
let _geomanMarkerPointer: any = null
let _originalGeomanMarkerSetLngLat: ((...args: any[]) => void) | null = null
let _snappingEnabled = true

export function registerGeomanMarker(mp: any, _marker: any, orig: (...args: any[]) => void): void {
  _geomanMarkerPointer = mp
  _originalGeomanMarkerSetLngLat = orig
}

export function unpatchGeomanMarker(): void {
  _snappingEnabled = false
  if (_geomanMarkerPointer?.marker && _originalGeomanMarkerSetLngLat) {
    _geomanMarkerPointer.marker.setLngLat = _originalGeomanMarkerSetLngLat
    ;(_geomanMarkerPointer.marker as any)._narsSnapPatchedInstance = false
  }
}

export function isSnappingEnabled(): boolean {
  return _snappingEnabled
}

export function setSnappingEnabled(v: boolean): void {
  _snappingEnabled = v
}
/* eslint-enable @typescript-eslint/no-explicit-any */

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
