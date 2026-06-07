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
