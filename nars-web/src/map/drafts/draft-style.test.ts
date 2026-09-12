import { describe, it, expect } from "vitest"
import {
  parseDraftGeometry,
  draftColor,
  DRAFT_ROAD_COLOR,
  DRAFT_BUILDING_COLOR,
  DRAFT_LINE_WIDTH,
} from "./draft-style"

describe("draftColor", () => {
  it("maps roads to the road hue and buildings to the raspberry hue", () => {
    expect(draftColor("road")).toBe(DRAFT_ROAD_COLOR)
    expect(draftColor("building")).toBe(DRAFT_BUILDING_COLOR)
  })
})

describe("parseDraftGeometry", () => {
  const roadGeoJson = `{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}`
  const buildingGeoJson = `{"type":"Polygon","coordinates":[[[36.71,2.95],[36.72,2.95],[36.72,2.96],[36.71,2.95]]]}`

  it("builds a dashed styled feature for a road draft", () => {
    const f = parseDraftGeometry("d1", "road", roadGeoJson, 0.9)
    expect(f).not.toBeNull()
    expect(f!.geometry.type).toBe("LineString")
    expect(f!.properties.draftId).toBe("d1")
    expect(f!.properties.lineColor).toBe(DRAFT_ROAD_COLOR)
    expect(f!.properties.lineWidth).toBe(DRAFT_LINE_WIDTH)
    expect(f!.properties.fillColor).toBeUndefined()
    expect(f!.properties.fillOpacity).toBeUndefined()
  })

  it("adds a translucent fill for building polygons", () => {
    const f = parseDraftGeometry("d2", "building", buildingGeoJson, 0.7)
    expect(f).not.toBeNull()
    expect(f!.geometry.type).toBe("Polygon")
    expect(f!.properties.featureType).toBe("building")
    expect(f!.properties.fillColor).toBe(DRAFT_BUILDING_COLOR)
    expect(f!.properties.fillOpacity).toBe(0.08)
  })

  it("rejects invalid JSON", () => {
    expect(parseDraftGeometry("d3", "road", "not json", 0.5)).toBeNull()
  })

  it("rejects geometry kinds drafts never carry", () => {
    expect(
      parseDraftGeometry("d4", "road", `{"type":"Point","coordinates":[36.7,2.9]}`, 0.5),
    ).toBeNull()
  })
})
