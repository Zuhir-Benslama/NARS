import type { LatLng } from "./coordinates"
import type { UserInfo } from "./user"

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
  counts: FeatureCounts
  cityCenterMode: "city_center" | "auto" | null
  cityCenterLatLng: LatLng | null
  user: UserInfo | null
  municipalityName: string
  loadError: boolean
  isLoading: boolean
  referenceRoadDbId: string | null
  referenceEntranceDbId: string | null
  boundaryEventsRegistered: boolean
}
