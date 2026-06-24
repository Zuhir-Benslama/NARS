import { defineStore } from "pinia"
import { PHASES } from "../phases"
import type { GeomanMarkerPointer } from "../map/core/geoman-types"

type LngLatInput = [number, number] | { lng: number; lat: number }
type SetLngLatFn = (lngLat: LngLatInput) => void

export const useDrawStore = defineStore("draw", {
  state: () => ({
    // Non-serializable (function/object references) — used only during an
    // active drawing session to patch/unpatch Geoman's marker setLngLat.
    geomanMarkerPointer: null as GeomanMarkerPointer | null,
    originalGeomanMarkerSetLngLat: null as SetLngLatFn | null,
    snappingEnabled: true,
    repatchMarkerPointer: null as (() => void) | null,
    drawingPhase: null as (typeof PHASES)[number] | null,
    savingFeature: false,
  }),

  actions: {
    registerGeomanMarker(
      mp: GeomanMarkerPointer,
      _marker: unknown,
      orig: SetLngLatFn,
    ): void {
      this.geomanMarkerPointer = mp
      this.originalGeomanMarkerSetLngLat = orig
    },
    unpatchGeomanMarker(): void {
      this.snappingEnabled = false
      if (this.geomanMarkerPointer?.marker && this.originalGeomanMarkerSetLngLat) {
        this.geomanMarkerPointer.marker.setLngLat = this.originalGeomanMarkerSetLngLat
        this.geomanMarkerPointer.marker._narsSnapPatchedInstance = false
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
