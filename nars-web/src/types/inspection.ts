export interface RoadInspectionData {
  roadTraffic: "high" | "medium" | "low"
  tradActivity: "high" | "medium" | "low"
  numLanes: number
  hasMedian: boolean
  hasVegetation: boolean
  isDeadEnd: boolean
  hasSidewalk: boolean
}

export interface EntranceInspectionData {
  hasEntrance: boolean
  hasNumberingPanel?: boolean
  numberCorrect?: boolean
  positionCorrect?: boolean
}

export interface NamingPanelInspectionData {
  hasLocation: boolean
  hasPanel?: boolean
  namingCorrect?: boolean
  positionCorrect?: boolean
}

export type InspectionType = "road" | "house_entrance" | "naming_panel"

export type EntranceStep =
  1 | 2 | 3 | 4 | "no_entrance" | "no_panel" | "wrong_number" | "wrong_position" | "good"

export type NamingPanelStep =
  1 | 2 | 3 | 4 | "no_location" | "no_panel" | "wrong_naming" | "wrong_position" | "good"

export type InspectionStatus = "good" | "issue"

export interface InspectionResult {
  id: string
  feature_id: string
  type: InspectionType
  data: RoadInspectionData | EntranceInspectionData | NamingPanelInspectionData
  status: "good" | "issue"
  created_at: string
}

export interface FieldFeature {
  id: string
  label: string
  type: InspectionType
}
