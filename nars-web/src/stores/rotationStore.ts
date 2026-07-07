import { defineStore } from "pinia"

export const useRotationStore = defineStore("rotation", {
  state: () => ({
    currentBearing: 0,
  }),

  actions: {
    setBearing(deg: number): void {
      this.currentBearing = ((deg % 360) + 360) % 360
    },
    resetRotation(): void {
      this.$reset()
    },
  },
})
