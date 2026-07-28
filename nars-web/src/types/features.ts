import type { LatLng } from "./coordinates"

export type FeatureTypeKey =
  | "areas"
  | "districts"
  | "cityCenter"
  | "roads"
  | "houseEntrances"
  | "publicBuildings"
  | "publicSpaces"
  | "namingPanels"

// ── Permissive bag-of-optional-fields (backward compat for stores / generic code) ──

export interface FeatureData {
  type: FeatureTypeKey
  label: string
  decisionNumber?: string
  decisionDate?: string
  coordinates?: LatLng[]
  lat?: number
  lng?: number
  radius?: number
  areaTypeKey?: string
  districtTypeKey?: string
  roadTypeKey?: string
  roadDbId?: string
  roadLabel?: string
  side?: "left" | "right"
  entranceNumber?: number
  mainEntranceDbId?: string
  mainEntranceLabel?: string
  bisNumber?: number
  entranceTypeKey?: "main_entrance" | "secondary_entrance"
  spaceTypeKey?: string
  sectorKey?: string
  buildingTypeKey?: string
  geometry?: string
}

// ── Strict per-type interfaces (only relevant fields per type) ─────────────────

interface CommonFields {
  type: FeatureTypeKey
  label: string
  coordinates?: LatLng[]
  geometry?: string
}

export interface AreaFeatureData extends CommonFields {
  type: "areas"
  decisionNumber: string
  decisionDate: string
  areaTypeKey: string
}
export interface DistrictFeatureData extends CommonFields {
  type: "districts"
  decisionNumber: string
  decisionDate: string
  districtTypeKey: string
}
export interface CityCenterFeatureData extends CommonFields {
  type: "cityCenter"
  decisionNumber: string
  decisionDate: string
  radius?: number
  lat?: number
  lng?: number
}
export interface RoadFeatureData extends CommonFields {
  type: "roads"
  decisionNumber: string
  decisionDate: string
  roadTypeKey: string
}
export interface HouseEntranceFeatureData extends CommonFields {
  type: "houseEntrances"
  entranceTypeKey: "main_entrance" | "secondary_entrance"
  roadDbId?: string
  roadLabel?: string
  side?: "left" | "right"
  entranceNumber?: number
  mainEntranceDbId?: string
  mainEntranceLabel?: string
  bisNumber?: number
  lat?: number
  lng?: number
}
export interface PublicBuildingFeatureData extends CommonFields {
  type: "publicBuildings"
  decisionNumber: string
  decisionDate: string
  sectorKey: string
  buildingTypeKey: string
}
export interface PublicSpaceFeatureData extends CommonFields {
  type: "publicSpaces"
  decisionNumber: string
  decisionDate: string
  spaceTypeKey: string
}
export interface NamingPanelFeatureData extends CommonFields {
  type: "namingPanels"
  lat?: number
  lng?: number
}

export type FeatureDataByType =
  | AreaFeatureData
  | DistrictFeatureData
  | CityCenterFeatureData
  | RoadFeatureData
  | HouseEntranceFeatureData
  | PublicBuildingFeatureData
  | PublicSpaceFeatureData
  | NamingPanelFeatureData

export interface LayerEntry<T = FeatureDataByType> {
  id: string
  dbId: string
  data: T
  type: "polygon" | "line" | "circle" | "marker"
}
