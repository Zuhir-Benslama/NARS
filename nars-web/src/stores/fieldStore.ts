import { defineStore } from "pinia"
import type { InspectionType } from "../types/inspection"

export interface SelectedFeature {
  id: string
  label: string
  type: InspectionType
}

export const useFieldStore = defineStore("field", {
  state: () => ({
    selectedFeature: null as SelectedFeature | null,
  }),

  getters: {
    hasSelection: (state) => state.selectedFeature !== null,
    featureType: (state) => state.selectedFeature?.type ?? null,
  },

  actions: {
    selectFeature(feature: SelectedFeature | null) {
      this.selectedFeature = feature
    },
    clearSelection() {
      this.selectedFeature = null
    },
  },
})
