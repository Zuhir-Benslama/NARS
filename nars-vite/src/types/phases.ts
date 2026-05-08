export type DrawType = "polygon" | "polyline" | "marker" | "circle"

export interface Phase {
  index: number
  key: string
  label: string
  drawType: DrawType
  color: string
  hint: string
  geometryType: "Polygon" | "LineString" | "Point"
}
