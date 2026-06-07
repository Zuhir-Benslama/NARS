// ─── LAYER STORE ──────────────────────────────────────────────────────────────
// Pinia store for map layer feature tracking.
// Replaces the non-reactive module-level `featureLayers` export.

import { defineStore } from "pinia"
import type { LayerEntry } from "../types"

export interface LayerState {
  areas: LayerEntry[]
  cityCenter: LayerEntry[]
  districts: LayerEntry[]
  roads: LayerEntry[]
  houseEntrances: LayerEntry[]
  publicBuildings: LayerEntry[]
  publicSpaces: LayerEntry[]
  namingPanels: LayerEntry[]
}

function createInitialState(): LayerState {
  return {
    areas: [],
    cityCenter: [],
    districts: [],
    roads: [],
    houseEntrances: [],
    publicBuildings: [],
    publicSpaces: [],
    namingPanels: [],
  }
}

export const useLayerStore = defineStore("layers", {
  state: (): LayerState => createInitialState(),

  getters: {
    mainEntrances: (state) =>
      state.houseEntrances.filter((e: LayerEntry) => e.data.entranceTypeKey === "main_entrance"),
    secondaryEntrances: (state) =>
      state.houseEntrances.filter(
        (e: LayerEntry) => e.data.entranceTypeKey === "secondary_entrance",
      ),

    // Computed counts
    areaCount: (state) => state.areas.length,
    cityCenterCount: (state) => state.cityCenter.length,
    districtCount: (state) => state.districts.length,
    roadCount: (state) => state.roads.length,
    mainEntranceCount: (state) =>
      state.houseEntrances.filter((e: LayerEntry) => e.data.entranceTypeKey === "main_entrance")
        .length,
    secondaryEntranceCount: (state) =>
      state.houseEntrances.filter(
        (e: LayerEntry) => e.data.entranceTypeKey === "secondary_entrance",
      ).length,
    publicBuildingCount: (state) => state.publicBuildings.length,
    publicSpaceCount: (state) => state.publicSpaces.length,
    namingPanelCount: (state) => state.namingPanels.length,
  },

  actions: {
    addFeature(layer: keyof LayerState, entry: LayerEntry) {
      this[layer].push(entry)
    },

    removeFeature(layer: keyof LayerState, dbId: string) {
      const idx = this[layer].findIndex((e) => e.dbId === dbId)
      if (idx !== -1) this[layer].splice(idx, 1)
    },

    updateFeature(layer: keyof LayerState, dbId: string, data: Partial<LayerEntry["data"]>) {
      const entry = this[layer].find((e) => e.dbId === dbId)
      if (entry) {
        Object.assign(entry.data, data)
      }
    },

    clearLayer(layer: keyof LayerState) {
      this[layer] = []
    },

    getFeature(dbId: string): LayerEntry | null {
      for (const features of Object.values(this.$state)) {
        if (Array.isArray(features)) {
          const found = features.find((e: LayerEntry) => e.dbId === dbId)
          if (found) return found
        }
      }
      return null
    },

    reset() {
      this.$reset()
    },
  },
})

// ─── SELECTION STATE ─────────────────────────────────────────────────────────
// The currently selected feature by left-click (separate from Pinia since
// it's transient and doesn't need store persistence).

export let selectedFeatureDbId: string | null = null

export function setSelectedFeature(dbId: string | null): void {
  selectedFeatureDbId = dbId
}
