import { defineStore } from "pinia"

export type MarkerSetLngLatFn = (lngLat: [number, number]) => void

export const useSnapStore = defineStore("snap", {
  state: () => ({
    // Master on/off toggle for the snapping feature. Single source of truth —
    // drawStore.snappingEnabled is a getter over this (see M4).
    snappingEnabled: true,
    crosshairActive: false,
    snapActive: false,
    snapLatLng: null as { lat: number; lng: number } | null,
    snapFrozen: false,
    snapRafId: null as number | null,
    snapPendingCoords: null as { x: number; y: number } | null,
    editDragActive: false,
    snapExclude: null as string | null,
    // The marker.setLngLat stashed before snapping patches it. Shared by the
    // draw marker patch and the edit marker patch (L5): both restore it on
    // unpatch, and edit-snap treats its presence as "already patched".
    origMarkerSetLngLat: null as MarkerSetLngLatFn | null,
  }),

  getters: {
    getFrozenSnapPos(): { lat: number; lng: number } | null {
      return this.snapFrozen && this.snapLatLng ? { ...this.snapLatLng } : null
    },
  },

  actions: {
    setSnappingEnabled(v: boolean): void {
      this.snappingEnabled = v
    },
    setEditDragActive(v: boolean): void {
      this.editDragActive = v
    },
    setSnapExclude(id: string | null): void {
      this.snapExclude = id
    },
    setOrigMarkerSetLngLat(fn: MarkerSetLngLatFn | null): void {
      this.origMarkerSetLngLat = fn
    },
    clearPendingCoords(): void {
      this.snapPendingCoords = null
    },
    patchSnapState(
      fields: Partial<{
        crosshairActive: boolean
        snapActive: boolean
        snapLatLng: { lat: number; lng: number } | null
        snapFrozen: boolean
        snapRafId: number | null
        snapPendingCoords: { x: number; y: number } | null
      }>,
    ): void {
      this.$patch(fields)
    },
    resetSnap(): void {
      if (this.snapRafId !== null) cancelAnimationFrame(this.snapRafId)
      this.$reset()
    },
  },
})
