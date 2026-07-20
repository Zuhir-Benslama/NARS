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

let _cachedFeatureMap: Map<string, { layer: keyof LayerState; entry: LayerEntry }> | null = null
let _featureMapDirty = true

export function resetLayerCache(): void {
  _cachedFeatureMap = null
  _featureMapDirty = true
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

export const LAYER_KEYS: (keyof LayerState)[] = [
  "areas",
  "cityCenter",
  "districts",
  "roads",
  "houseEntrances",
  "publicBuildings",
  "publicSpaces",
  "namingPanels",
]

export const useLayerStore = defineStore("layer", {
  state: (): LayerState => createInitialState(),

  getters: {
    mainEntrances: (state) =>
      state.houseEntrances.filter((e: LayerEntry) => e.data.entranceTypeKey === "main_entrance"),
    secondaryEntrances: (state) =>
      state.houseEntrances.filter(
        (e: LayerEntry) => e.data.entranceTypeKey === "secondary_entrance",
      ),

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

    /** Map of dbId → { layer, entry } for O(1) lookups via getFeature(). Cached. */
    _featureMap(state): Map<string, { layer: keyof LayerState; entry: LayerEntry }> {
      if (!_featureMapDirty && _cachedFeatureMap) {
        return _cachedFeatureMap
      }
      const map = new Map<string, { layer: keyof LayerState; entry: LayerEntry }>()
      for (const key of LAYER_KEYS) {
        for (const entry of state[key]) {
          map.set(entry.dbId, { layer: key, entry })
        }
      }
      _cachedFeatureMap = map
      _featureMapDirty = false
      return map
    },
  },

  actions: {
    addFeature(layer: keyof LayerState, entry: LayerEntry) {
      this[layer].push(entry)
      _featureMapDirty = true
    },

    removeFeature(layer: keyof LayerState, dbId: string) {
      const idx = this[layer].findIndex((e) => e.dbId === dbId)
      if (idx !== -1) {
        this[layer].splice(idx, 1)
        _featureMapDirty = true
      }
    },

    updateFeature(layer: keyof LayerState, dbId: string, data: Partial<LayerEntry["data"]>) {
      const entry = this[layer].find((e) => e.dbId === dbId)
      if (entry) {
        Object.assign(entry.data, data)
        _featureMapDirty = true
      }
    },

    clearLayer(layer: keyof LayerState) {
      this[layer] = []
      _featureMapDirty = true
    },

    getFeature(dbId: string): LayerEntry | null {
      return this._featureMap.get(dbId)?.entry ?? null
    },

    reset() {
      this.$reset()
      _featureMapDirty = true
    },
  },
})
