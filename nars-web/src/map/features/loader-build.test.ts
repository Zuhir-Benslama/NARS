import { describe, it, expect, vi } from "vitest"
import { buildGeoJsonFeature } from "./loader-build"
import type { FeatureData } from "../../types/features"
import type { Phase } from "../../types"

vi.mock("../draw/draw-save", () => ({
  getFeatureStyle: () => ({
    fillColor: "#8e44ad",
    fillOpacity: 0.1,
    lineColor: "#8e44ad",
    lineWidth: 2,
  }),
}))

import { PHASES } from "../../phases"

const areasPhase = PHASES[0]
const cityCenterPhase = PHASES.find((p: Phase) => p.key === "cityCenter")!
const roadPhase = PHASES.find((p: Phase) => p.key === "roads")!

function data(overrides: Partial<FeatureData> = {}): FeatureData {
  return {
    type: "test",
    label: "Test Feature",
    decisionNumber: "123",
    decisionDate: "2024-01-01",
    ...overrides,
  }
}

describe("buildGeoJsonFeature", () => {
  it("builds a Point feature from lat/lng data", () => {
    const result = buildGeoJsonFeature("db-1", data({ lat: 10, lng: 20 }), areasPhase)
    expect(result).not.toBeNull()
    expect(result!.geometry.type).toBe("Point")
    const coords = (result!.geometry as GeoJSON.Point).coordinates
    expect(coords).toEqual([20, 10])
    expect(result!.properties.dbId).toBe("db-1")
    expect(result!.properties.label).toBe("Test Feature")
  })

  it("builds a LineString for polyline phases with coordinates", () => {
    const result = buildGeoJsonFeature(
      "db-2",
      data({
        coordinates: [
          { lat: 1, lng: 2 },
          { lat: 3, lng: 4 },
        ],
      }),
      roadPhase,
    )
    expect(result).not.toBeNull()
    expect(result!.geometry.type).toBe("LineString")
    const coords = (result!.geometry as GeoJSON.LineString).coordinates
    expect(coords).toEqual([
      [2, 1],
      [4, 3],
    ])
  })

  it("builds a Polygon for polygon phases with coordinates", () => {
    const result = buildGeoJsonFeature(
      "db-3",
      data({
        coordinates: [
          { lat: 1, lng: 2 },
          { lat: 3, lng: 4 },
          { lat: 5, lng: 6 },
        ],
      }),
      areasPhase,
    )
    expect(result).not.toBeNull()
    expect(result!.geometry.type).toBe("Polygon")
    const poly = result!.geometry as GeoJSON.Polygon
    expect(poly.coordinates[0].length).toBe(4)
    expect(result!.properties.geomType).toBe("Polygon")
  })

  it("builds a LineString circle ring for cityCenter with radius", () => {
    const result = buildGeoJsonFeature(
      "db-4",
      data({ lat: 10, lng: 20, radius: 500 }),
      cityCenterPhase,
    )
    expect(result).not.toBeNull()
    expect(result!.geometry.type).toBe("LineString")
    expect(result!.properties.radius).toBe(500)
  })

  it("builds a Point for cityCenter without radius", () => {
    const result = buildGeoJsonFeature("db-5", data({ lat: 10, lng: 20 }), cityCenterPhase)
    expect(result).not.toBeNull()
    expect(result!.geometry.type).toBe("Point")
  })

  it("returns null when no coordinates are present", () => {
    const result = buildGeoJsonFeature("db-6", data(), areasPhase)
    expect(result).toBeNull()
  })

  it("sanitizes the label", () => {
    const result = buildGeoJsonFeature(
      "db-7",
      data({ lat: 1, lng: 2, label: "<script>alert('xss')</script>" }),
      areasPhase,
    )
    expect(result).not.toBeNull()
    expect(result!.properties.label).not.toContain("<script>")
  })
})
