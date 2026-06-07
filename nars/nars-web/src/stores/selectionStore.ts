import { defineStore } from "pinia"

interface SelectionState {
  selectedFeatureDbId: string | null
}

export const useSelectionStore = defineStore("selection", {
  state: (): SelectionState => ({
    selectedFeatureDbId: null,
  }),

  actions: {
    selectFeature(dbId: string | null) {
      this.selectedFeatureDbId = dbId
    },
  },
})
