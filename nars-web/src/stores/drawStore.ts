import { defineStore } from "pinia"
import { PHASES } from "../phases"
import type { GeomanMarkerPointer } from "../map/core/geoman-types"
import { useSnapStore } from "./snapStore"

type LngLatInput = [number, number] | { lng: number; lat: number }
type SetLngLatFn = (lngLat: LngLatInput) => void

export const useDrawStore = defineStore("draw", {
  state: () => ({
    geomanMarkerPointer: null as GeomanMarkerPointer | null,
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

  getters: {
    // Single source of truth lives in snapStore (M4). drawStore keeps this
    // getter so existing callers (draw-state.ts) read one consistent flag.
    snappingEnabled(): boolean {
      return useSnapStore().snappingEnabled
    },
  },

  actions: {
    registerGeomanMarker(mp: GeomanMarkerPointer, _marker: unknown, orig: SetLngLatFn): void {
      this.geomanMarkerPointer = mp
      useSnapStore().setOrigMarkerSetLngLat(orig)
    },
    unpatchGeomanMarker(): void {
      const marker = this.geomanMarkerPointer?.marker as Record<string, unknown> | null | undefined
      const orig = useSnapStore().origMarkerSetLngLat
      if (marker && orig) {
        marker.setLngLat = orig
      }
      useSnapStore().setOrigMarkerSetLngLat(null)
    },
    setSnappingEnabled(v: boolean): void {
      useSnapStore().setSnappingEnabled(v)
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
    setModeSwitchToken(v: number): void {
      this.modeSwitchToken = v
    },
    setEdgePollId(v: ReturnType<typeof setInterval> | null): void {
      this.edgePollId = v
    },
    setEdgeTimeoutId(v: ReturnType<typeof setTimeout> | null): void {
      this.edgeTimeoutId = v
    },
    setCleanupDrawWatcher(fn: (() => void) | null): void {
      this.cleanupDrawWatcher = fn
    },
    resetDraw(): void {
      if (this.edgePollId !== null) clearInterval(this.edgePollId)
      if (this.edgeTimeoutId !== null) clearTimeout(this.edgeTimeoutId)
      // Cancel any pending marker re-patch rAF too — repatchMarkerPointer
      // keeps polling up to ~5 s and would re-write geomanMarkerPointer into
      // freshly-reset state if left running.
      if (this.patchRafRef.current !== null) {
        cancelAnimationFrame(this.patchRafRef.current)
        this.patchRafRef.current = null
      }
      this.cleanupDrawWatcher?.()
      useSnapStore().setOrigMarkerSetLngLat(null)
      this.$reset()
    },
  },
})
