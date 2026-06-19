// ─── APP STORE ────────────────────────────────────────────────────────────────
// Pinia store for application-level state: phase, user, counts, loading.

import { defineStore } from "pinia"
import type { AppStoreState, FeatureCounts, UserInfo } from "../types"
import { useLayerStore } from "./layerStore"

export const useAppStore = defineStore("app", {
  state: (): AppStoreState => ({
    currentPhase: 0,
    counts: {
      areas: 0,
      cityCenter: 0,
      districts: 0,
      roads: 0,
      mainEntrances: 0,
      secondaryEntrances: 0,
      publicBuildings: 0,
      publicSpaces: 0,
      namingPanels: 0,
    },
    cityCenterMode: null,
    cityCenterLatLng: null,
    user: null,
    municipalityName: "",
    loadError: false,
    isLoading: false,
    referenceRoadDbId: null,
    referenceEntranceDbId: null,
  }),

  getters: {
    isAuthenticated: (state) => state.user !== null,
    isAdminUser: (state) =>
      state.user !== null &&
      state.user.role !== "commune_user" &&
      state.user.role !== "field_worker",
    communeName: (state) =>
      state.municipalityName || state.user?.commune?.name_fr || state.user?.commune?.name_ar || "",
  },

  actions: {
    setUser(user: UserInfo | null) {
      this.user = user
      this.municipalityName = user?.commune?.name_fr ?? user?.commune?.name_ar ?? ""
    },
    setLoading(isLoading: boolean) {
      this.isLoading = isLoading
    },
    setLoadError(hasError: boolean) {
      this.loadError = hasError
    },
    updateCounts(counts: FeatureCounts) {
      this.counts = counts
    },
    syncCounts() {
      const layerStore = useLayerStore()
      this.counts = {
        areas: layerStore.areaCount,
        cityCenter: layerStore.cityCenterCount,
        districts: layerStore.districtCount,
        roads: layerStore.roadCount,
        mainEntrances: layerStore.mainEntranceCount,
        secondaryEntrances: layerStore.secondaryEntranceCount,
        publicBuildings: layerStore.publicBuildingCount,
        publicSpaces: layerStore.publicSpaceCount,
        namingPanels: layerStore.namingPanelCount,
      }
    },
    reset() {
      this.$reset()
    },
  },
})
