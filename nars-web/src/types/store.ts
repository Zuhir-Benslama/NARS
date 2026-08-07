import type { UserInfo } from "./user"

// FeatureCounts deliberately splits houseEntrances into mainEntrances +
// secondaryEntrances since they are counted separately in the UI.
// All other PHASES keys have a 1:1 field here.
export interface FeatureCounts {
  areas: number
  cityCenter: number
  districts: number
  roads: number
  mainEntrances: number
  secondaryEntrances: number
  publicBuildings: number
  publicSpaces: number
  namingPanels: number
}

export interface AppStoreState {
  currentPhase: number
  user: UserInfo | null
  loadError: boolean
  isLoading: boolean
  referenceRoadDbId: string | null
  referenceEntranceDbId: string | null
  boundaryEventsRegistered: boolean
}
