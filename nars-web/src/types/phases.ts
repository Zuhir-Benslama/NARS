import type { FeatureTypeKey } from "./features"

export type DrawType = "polygon" | "polyline" | "marker" | "circle"

export interface Phase {
  index: number
  key: FeatureTypeKey
  label: string
  drawType: DrawType
  color: string
  hint: string
  geometryType: "Polygon" | "LineString" | "Point"
}
