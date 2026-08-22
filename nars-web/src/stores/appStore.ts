// ─── APP STORE ────────────────────────────────────────────────────────────────
// Pinia store for application-level state: phase, user, counts, loading.

import { defineStore } from "pinia"
import type { AppStoreState, FeatureCounts, UserInfo, LatLng } from "../types"
import { useLayerStore } from "./layerStore"

export const useAppStore = defineStore("app", {
  state: (): AppStoreState => ({
    currentPhase: 0,
    user: null,
    loadError: false,
    isLoading: false,
    referenceRoadDbId: null,
    referenceEntranceDbId: null,
    boundaryEventsRegistered: false,
  }),

  getters: {
    isAuthenticated: (state) => state.user !== null,
    isAdminUser: (state) =>
      state.user !== null &&
      ["national_admin", "wilaya_admin", "daira_admin"].includes(state.user.role),
    // Mirrors the server's UserManagementRoles policy (AdminUserController):
    // commune_user may manage its own field_worker accounts even though it is
    // not an "admin" for routing/dashboard purposes.
    canManageUsers: (state) =>
      state.user !== null &&
      ["national_admin", "wilaya_admin", "daira_admin", "commune_user"].includes(state.user.role),
    communeName: (state) => state.user?.commune?.name_fr || state.user?.commune?.name_ar || "",
    // Derived from the layer store (single source of truth for features).
    // Previously duplicated state that had to be manually re-synced at every
    // mutation site (and was missed in several).
    cityCenterLatLng: (): LatLng | null => {
      const cc = useLayerStore().cityCenter[0]
      if (!cc) return null
      const d = cc.data as { lat?: number; lng?: number }
      return d.lat != null && d.lng != null ? { lat: d.lat, lng: d.lng } : null
    },
    counts: (): FeatureCounts => {
      const layerStore = useLayerStore()
      return {
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
  },

  actions: {
    setUser(user: UserInfo | null) {
      this.user = user
    },
    setLoading(isLoading: boolean) {
      this.isLoading = isLoading
    },
    setLoadError(hasError: boolean) {
      this.loadError = hasError
    },
    setCurrentPhase(index: number) {
      this.currentPhase = index
    },
    setReferenceRoad(dbId: string | null) {
      this.referenceRoadDbId = dbId
    },
    setReferenceEntrance(dbId: string | null) {
      this.referenceEntranceDbId = dbId
    },
    setBoundaryEventsRegistered(v: boolean) {
      this.boundaryEventsRegistered = v
    },
  },
})

export function resetAppStore(): void {
  useAppStore().$reset()
}
