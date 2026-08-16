export interface ModalState {
  visible: boolean
  phaseIndex: number | null
  isEdit: boolean
  editDbId: string | null
  label: string
  decisionNumber: string
  decisionDate: string
  errors: Record<string, string>
  areaTypeKey: string
  mainUrbanExists: boolean
  districtTypeKey: string
  roadTypeKey: string
  spaceTypeKey: string
  sectorKey: string
  buildingTypeKey: string
  radius: number | null
  currentModalFeatureId: string | null
}

export interface AreaModalResult {
  type: "areas"
  label: string
  decisionNumber: string
  decisionDate: string
  areaTypeKey?: string
}

export interface DistrictModalResult {
  type: "districts"
  label: string
  decisionNumber: string
  decisionDate: string
  districtTypeKey?: string
}

export interface CityCenterModalResult {
  type: "cityCenter"
  label: string
  radius?: number
}

export interface RoadModalResult {
  type: "roads"
  label: string
  decisionNumber: string
  decisionDate: string
  roadTypeKey?: string
}

export interface HouseEntranceModalResult {
  type: "houseEntrances"
  label: string
  entranceTypeKey?: "main_entrance" | "secondary_entrance"
  roadDbId?: string
  roadLabel?: string
  side?: "left" | "right"
  entranceNumber?: number
  mainEntranceDbId?: string
  mainEntranceLabel?: string
  bisNumber?: number
}

export interface PublicBuildingModalResult {
  type: "publicBuildings"
  label: string
  decisionNumber: string
  decisionDate: string
  sectorKey?: string
  buildingTypeKey?: string
}

export interface PublicSpaceModalResult {
  type: "publicSpaces"
  label: string
  decisionNumber: string
  decisionDate: string
  spaceTypeKey?: string
}

export interface NamingPanelModalResult {
  type: "namingPanels"
  label: string
  decisionNumber: string
  decisionDate: string
}

export type ModalResult =
  | AreaModalResult
  | DistrictModalResult
  | CityCenterModalResult
  | RoadModalResult
  | HouseEntranceModalResult
  | PublicBuildingModalResult
  | PublicSpaceModalResult
  | NamingPanelModalResult
