import { defineStore } from "pinia"
import { PHASES } from "../phases"
import type { GeomanMarkerPointer } from "../map/core/geoman-types"

type LngLatInput = [number, number] | { lng: number; lat: number }
type SetLngLatFn = (lngLat: LngLatInput) => void

export const useDrawStore = defineStore("draw", {
  state: () => ({
    geomanMarkerPointer: null as GeomanMarkerPointer | null,
    originalGeomanMarkerSetLngLat: null as SetLngLatFn | null,
    snappingEnabled: true,
    repatchMarkerPointer: null as (() => void) | null,
    drawingPhase: null as (typeof PHASES)[number] | null,
    savingFeature: false,
    lastPhaseKey: null as string | null,
    modeSwitchToken: 0,
    cleanupDrawWatcher: null as (() => void) | null,
    patchRafRef: { current: null } as { current: number | null },
    edgePollId: null as ReturnType<typeof setInterval> | null,
    edgeTimeoutId: null as ReturnType<typeof setTimeout> | null,
  }),

  actions: {
    registerGeomanMarker(mp: GeomanMarkerPointer, _marker: unknown, orig: SetLngLatFn): void {
      this.geomanMarkerPointer = mp
      this.originalGeomanMarkerSetLngLat = orig
    },
    unpatchGeomanMarker(): void {
      this.snappingEnabled = false
      const marker = this.geomanMarkerPointer?.marker as Record<string, unknown> | null | undefined
      if (marker && this.originalGeomanMarkerSetLngLat) {
        marker.setLngLat = this.originalGeomanMarkerSetLngLat
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
    setLastPhaseKey(key: string | null): void {
      this.lastPhaseKey = key
    },
    incrementModeSwitchToken(): number {
      return ++this.modeSwitchToken
    },
    setCleanupDrawWatcher(fn: (() => void) | null): void {
      this.cleanupDrawWatcher = fn
    },
    resetDraw(): void {
      if (this.edgePollId !== null) clearInterval(this.edgePollId)
      if (this.edgeTimeoutId !== null) clearTimeout(this.edgeTimeoutId)
      this.cleanupDrawWatcher?.()
      this.$reset()
    },
  },
})
