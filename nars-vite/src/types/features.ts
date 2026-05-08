import type { LatLng } from "./coordinates"

export interface FeatureData {
  type: string
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

export interface LayerEntry {
  id: string
  dbId: string
  data: FeatureData
  type: "polygon" | "line" | "circle" | "marker"
}
