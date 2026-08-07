import { defineStore } from "pinia"
import type {
  AreaFeatureData,
  CityCenterFeatureData,
  DistrictFeatureData,
  FeatureDataByType,
  HouseEntranceFeatureData,
  LayerEntry,
  NamingPanelFeatureData,
  PublicBuildingFeatureData,
  PublicSpaceFeatureData,
  RoadFeatureData,
} from "../types"

export interface LayerState {
  areas: LayerEntry<AreaFeatureData>[]
  cityCenter: LayerEntry<CityCenterFeatureData>[]
  districts: LayerEntry<DistrictFeatureData>[]
  roads: LayerEntry<RoadFeatureData>[]
  houseEntrances: LayerEntry<HouseEntranceFeatureData>[]
  publicBuildings: LayerEntry<PublicBuildingFeatureData>[]
  publicSpaces: LayerEntry<PublicSpaceFeatureData>[]
  namingPanels: LayerEntry<NamingPanelFeatureData>[]
}

export function resetLayerCache(): void {
  useLayerStore().$reset()
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

const isMainEntrance = (e: LayerEntry<HouseEntranceFeatureData>) =>
  e.data.entranceTypeKey === "main_entrance"
const isSecondaryEntrance = (e: LayerEntry<HouseEntranceFeatureData>) =>
  e.data.entranceTypeKey === "secondary_entrance"

export const useLayerStore = defineStore("layer", {
  state: (): LayerState => createInitialState(),

  getters: {
    mainEntrances: (state) => state.houseEntrances.filter(isMainEntrance),
    secondaryEntrances: (state) => state.houseEntrances.filter(isSecondaryEntrance),

    areaCount: (state) => state.areas.length,
    cityCenterCount: (state) => state.cityCenter.length,
    districtCount: (state) => state.districts.length,
    roadCount: (state) => state.roads.length,
    mainEntranceCount: (state) => state.houseEntrances.filter(isMainEntrance).length,
    secondaryEntranceCount: (state) => state.houseEntrances.filter(isSecondaryEntrance).length,
    publicBuildingCount: (state) => state.publicBuildings.length,
    publicSpaceCount: (state) => state.publicSpaces.length,
    namingPanelCount: (state) => state.namingPanels.length,

    /** Map of dbId → { layer, entry } for O(1) lookups via getFeature(). */
    _featureMap(state): Map<string, { layer: keyof LayerState; entry: LayerEntry }> {
      const map = new Map<string, { layer: keyof LayerState; entry: LayerEntry }>()
      for (const key of LAYER_KEYS) {
        for (const entry of state[key]) {
          map.set(entry.dbId, { layer: key, entry })
        }
      }
      return map
    },
  },

  actions: {
    addFeature(layer: keyof LayerState, entry: LayerEntry) {
      ;(this[layer] as unknown as LayerEntry[]).push(entry)
    },

    removeFeature(layer: keyof LayerState, dbId: string) {
      const idx = (this[layer] as unknown as LayerEntry[]).findIndex((e) => e.dbId === dbId)
      if (idx !== -1) {
        ;(this[layer] as unknown as LayerEntry[]).splice(idx, 1)
      }
    },

    updateFeature(layer: keyof LayerState, dbId: string, data: Partial<FeatureDataByType>) {
      const entry = (this[layer] as unknown as LayerEntry[]).find((e) => e.dbId === dbId)
      if (entry) {
        Object.assign(entry.data, data)
      }
    },

    // Lookup by dbId across all layers (via _featureMap) and patch the entry's
    // data. Used by callers that only hold the feature id (e.g. geoman-events).
    updateFeatureData(dbId: string, data: Partial<FeatureDataByType>) {
      const found = this._featureMap.get(dbId)
      if (found) {
        Object.assign(found.entry.data, data)
      }
    },

    clearLayer(layer: keyof LayerState) {
      this[layer] = []
    },

    getFeature(dbId: string): LayerEntry | null {
      return this._featureMap.get(dbId)?.entry ?? null
    },

    reset() {
      this.$reset()
    },
  },
})
