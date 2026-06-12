import { defineStore } from "pinia"
import { PHASES } from "../phases"

export const useDrawStore = defineStore("draw", {
  state: () => ({
    geomanMarkerPointer: null as Record<string, unknown> | null,
    originalGeomanMarkerSetLngLat: null as ((...args: unknown[]) => void) | null,
    snappingEnabled: true,
    repatchMarkerPointer: null as (() => void) | null,
    drawingPhase: null as (typeof PHASES)[number] | null,
    savingFeature: false,
  }),

  actions: {
    registerGeomanMarker(
      mp: Record<string, unknown>,
      _marker: unknown,
      orig: (...args: unknown[]) => void,
    ): void {
      this.geomanMarkerPointer = mp
      this.originalGeomanMarkerSetLngLat = orig
    },
    unpatchGeomanMarker(): void {
      this.snappingEnabled = false
      if (this.geomanMarkerPointer?.marker && this.originalGeomanMarkerSetLngLat) {
        const marker = this.geomanMarkerPointer.marker as Record<string, unknown>
        marker.setLngLat = this.originalGeomanMarkerSetLngLat
        marker._narsSnapPatchedInstance = false
      }
    },
    setSnappingEnabled(v: boolean): void {
      this.snappingEnabled = v
    },
    setRepatchMarkerPointer(fn: () => void): void {
      this.repatchMarkerPointer = fn
    },
    repatchMarker(): void {
      this.repatchMarkerPointer?.()
    },
    setDrawingPhase(phase: (typeof PHASES)[number] | null): void {
      this.drawingPhase = phase
    },
    setSavingFeature(v: boolean): void {
      this.savingFeature = v
    },
    resetDraw(): void {
      this.$reset()
    },
  },
})
