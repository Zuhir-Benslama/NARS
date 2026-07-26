import { defineStore } from "pinia"

export const useSnapStore = defineStore("snap", {
  state: () => ({
    crosshairActive: false,
    snapActive: false,
    snapLatLng: null as { lat: number; lng: number } | null,
    snapFrozen: false,
    snapRafId: null as number | null,
    snapPendingCoords: null as { x: number; y: number } | null,
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
    setEditModeActive(v: boolean): void {
      this.editModeActive = v
    },
    setEditDragActive(v: boolean): void {
      this.editDragActive = v
    },
    clearPendingCoords(): void {
      this.snapPendingCoords = null
    },
    resetSnap(): void {
      if (this.snapRafId !== null) cancelAnimationFrame(this.snapRafId)
      this.$reset()
    },
  },
})
