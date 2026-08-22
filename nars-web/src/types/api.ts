import type { FeatureData } from "./features"

export interface SaveResult {
  ok: boolean
  error?: string
  data?: { id: string }
}

export interface ValidateRoadResponse {
  valid: boolean
  error: string | null
}

export interface ValidateDistrictResponse {
  valid: boolean
  error: string | null
}

export interface DistrictCoverageResponse {
  covered: boolean
  message: string
}

export interface RoadSideResponse {
  side: "left" | "right"
  suggestedNumber: number
}

export interface DbFeature {
  id: string
  layer: string
  feature_type: string
  label: string
  data: FeatureData | string
  created_at: string
}
