export interface ModalState {
  visible: boolean
  phaseIndex: number | null
  isEdit: boolean
  editDbId: string | null
  label: string
  labelAr: string
  labelFr: string
  labelEn: string
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
  labelAr?: string
  labelFr?: string
  labelEn?: string
  decisionNumber: string
  decisionDate: string
  areaTypeKey?: string
}

export interface DistrictModalResult {
  type: "districts"
  label: string
  labelAr?: string
  labelFr?: string
  labelEn?: string
  decisionNumber: string
  decisionDate: string
  districtTypeKey?: string
}

export interface CityCenterModalResult {
  type: "cityCenter"
  label: string
  labelAr?: string
  labelFr?: string
  labelEn?: string
  radius?: number
}

export interface RoadModalResult {
  type: "roads"
  label: string
  labelAr?: string
  labelFr?: string
  labelEn?: string
  decisionNumber: string
  decisionDate: string
  roadTypeKey?: string
}

export interface HouseEntranceModalResult {
  type: "houseEntrances"
  label: string
  labelAr?: string
  labelFr?: string
  labelEn?: string
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
  labelAr?: string
  labelFr?: string
  labelEn?: string
  decisionNumber: string
  decisionDate: string
  sectorKey?: string
  buildingTypeKey?: string
}

export interface PublicSpaceModalResult {
  type: "publicSpaces"
  label: string
  labelAr?: string
  labelFr?: string
  labelEn?: string
  decisionNumber: string
  decisionDate: string
  spaceTypeKey?: string
}

export interface NamingPanelModalResult {
  type: "namingPanels"
  label: string
  labelAr?: string
  labelFr?: string
  labelEn?: string
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
