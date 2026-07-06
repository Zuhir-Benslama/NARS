import { defineStore } from "pinia"

export const useSnapStore = defineStore("snap", {
  state: () => ({
    crosshairActive: false,
    snapActive: false,
    snapLatLng: null as { lat: number; lng: number } | null,
    snapFrozen: false,
    snapRafId: null as number | null,
    snapPendingEvent: null as MouseEvent | null,
    editModeActive: false,
    editDragActive: false,
    snapExclude: null as string | null,
  }),

  getters: {
    isSnapFrozen(): boolean {
      return this.snapFrozen
    },
    getFrozenSnapPos(): { lat: number; lng: number } | null {
      return this.snapFrozen && this.snapLatLng ? { ...this.snapLatLng } : null
    },
  },

  actions: {
    enableCrosshair(): void {
      if (this.crosshairActive) return
      this.crosshairActive = true
    },
    disableCrosshair(): void {
      if (!this.crosshairActive) return
      this.crosshairActive = false
    },
    setEditModeActive(v: boolean): void {
      this.editModeActive = v
    },
    setEditDragActive(v: boolean): void {
      this.editDragActive = v
    },
    clearPendingEvent(): void {
      this.snapPendingEvent = null
    },
    resetSnap(): void {
      this.$reset()
    },
  },
})
