import type { FeatureData } from "./features"

export interface RoadOption {
  idx: number
  label: string
  dbId: string
}

export interface EntranceOption {
  idx: number
  label: string
  dbId: string
}

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
  entranceTypeKey: "main_entrance" | "secondary_entrance"
  roadOptions: RoadOption[]
  selectedRoadIdx: number | ""
  entranceSide: "left" | "right" | null
  entranceNumber: number | null
  entranceSideLoading: boolean
  mainEntranceOptions: EntranceOption[]
  selectedMainIdx: number | ""
  bisNumber: number | null
  spaceTypeKey: string
  sectorKey: string
  buildingTypeKey: string
  radius: number | null
  currentModalFeatureId: string | null
}

export type ModalResult = Omit<FeatureData, "type" | "coordinates" | "lat" | "lng">
