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

export interface FeatureData {
  type: FeatureTypeKey
  label: string
  decisionNumber: string
  decisionDate: string
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

// Per-type discriminated unions for narrowing by `type` in new code.
export interface AreaFeatureData extends FeatureData {
  type: "areas"
}
export interface DistrictFeatureData extends FeatureData {
  type: "districts"
}
export interface CityCenterFeatureData extends FeatureData {
  type: "cityCenter"
}
export interface RoadFeatureData extends FeatureData {
  type: "roads"
}
export interface HouseEntranceFeatureData extends FeatureData {
  type: "houseEntrances"
}
export interface PublicBuildingFeatureData extends FeatureData {
  type: "publicBuildings"
}
export interface PublicSpaceFeatureData extends FeatureData {
  type: "publicSpaces"
}
export interface NamingPanelFeatureData extends FeatureData {
  type: "namingPanels"
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

export interface LayerEntry {
  id: string
  dbId: string
  data: FeatureData
  type: "polygon" | "line" | "circle" | "marker"
}
