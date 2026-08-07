import { defineStore } from "pinia"

interface SelectionState {
  selectedFeatureDbId: string | null
}

export const useSelectionStore = defineStore("selection", {
  state: (): SelectionState => ({
    selectedFeatureDbId: null,
  }),

  actions: {
    setSelectedFeatureDbId(dbId: string | null) {
      this.selectedFeatureDbId = dbId
    },
  },
})

export function resetSelectionStore(): void {
  useSelectionStore().$reset()
}
