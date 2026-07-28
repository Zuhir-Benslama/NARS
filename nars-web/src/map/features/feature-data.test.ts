import { describe, it, expect } from "vitest"
import { buildFeatureData, toApiSaveShape } from "./feature-data"
import type { FeatureData } from "../../types"
import type { ModalResult } from "../../types/modal"
import type { PHASES } from "../../phases"

function makePhase(key: string): (typeof PHASES)[number] {
  return { key } as (typeof PHASES)[number]
}

function areaModal(overrides: Partial<{ label: string; decisionNumber: string; decisionDate: string; areaTypeKey: string }> = {}): ModalResult {
  return { type: "areas", label: "Test Feature", decisionNumber: "123/45", decisionDate: "2024-01-15", areaTypeKey: "central_urban", ...overrides }
}

function roadsModal(overrides: Partial<{ label: string; decisionNumber: string; decisionDate: string; roadTypeKey: string }> = {}): ModalResult {
  return { type: "roads", label: "Test Feature", decisionNumber: "123/45", decisionDate: "2024-01-15", roadTypeKey: "street", ...overrides }
}

function houseEntranceModal(overrides: Partial<{ label: string }> = {}): ModalResult {
  return { type: "houseEntrances", label: "Test Feature", ...overrides }
}

function cityCenterModal(overrides: Partial<{ label: string; radius: number }> = {}): ModalResult {
  return { type: "cityCenter", label: "Test Feature", ...overrides }
}

describe("feature-data", () => {
  describe("buildFeatureData", () => {
    it("handles Point geometry", () => {
      const geometry: GeoJSON.Point = {
        type: "Point",
        coordinates: [127.5, 36.0],
      }
      const result = buildFeatureData(geometry, makePhase("houseEntrances"), houseEntranceModal()) as FeatureData

      expect(result.lat).toBe(36.0)
      expect(result.lng).toBe(127.5)
      expect(result.coordinates).toEqual([{ lat: 36.0, lng: 127.5 }])
      expect(result.type).toBe("houseEntrances")
    })

    it("handles LineString geometry", () => {
      const geometry: GeoJSON.LineString = {
        type: "LineString",
        coordinates: [
          [127.0, 36.0],
          [127.1, 36.1],
        ],
      }
      const result = buildFeatureData(geometry, makePhase("roads"), roadsModal())

      expect(result.coordinates).toEqual([
        { lat: 36.0, lng: 127.0 },
        { lat: 36.1, lng: 127.1 },
      ])
      expect(result.type).toBe("roads")
    })

    it("handles Polygon geometry", () => {
      const geometry: GeoJSON.Polygon = {
        type: "Polygon",
        coordinates: [
          [
            [127.0, 36.0],
            [127.1, 36.0],
            [127.05, 36.1],
            [127.0, 36.0],
          ],
        ],
      }
      const result = buildFeatureData(geometry, makePhase("areas"), areaModal())

      expect(result.coordinates).toHaveLength(4)
      expect(result.coordinates![0]).toEqual({ lat: 36.0, lng: 127.0 })
      expect(result.type).toBe("areas")
    })

    it("handles MultiPolygon geometry (flattens to first ring)", () => {
      const geometry: GeoJSON.MultiPolygon = {
        type: "MultiPolygon",
        coordinates: [
          [
            [
              [127.0, 36.0],
              [127.1, 36.0],
              [127.05, 36.1],
              [127.0, 36.0],
            ],
          ],
        ],
      }
      const result = buildFeatureData(geometry, makePhase("areas"), areaModal())

      expect(result.coordinates).toHaveLength(4)
    })

    it("returns base data for unknown geometry", () => {
      const geometry = {
        type: "UnknownType",
        coordinates: [],
      } as unknown as GeoJSON.Geometry
      const result = buildFeatureData(geometry, makePhase("areas"), areaModal())

      expect(result.coordinates).toBeUndefined()
      expect(result.type).toBe("areas")
      expect(result.label).toBe("Test Feature")
    })

    it("includes modal fields in result", () => {
      const geometry: GeoJSON.Point = {
        type: "Point",
        coordinates: [127.5, 36.0],
      }
      const modal = roadsModal({ decisionNumber: "456/78", decisionDate: "2024-06-01", roadTypeKey: "highway" })
      const result = buildFeatureData(geometry, makePhase("roads"), modal) as FeatureData

      expect(result.decisionNumber).toBe("456/78")
      expect(result.decisionDate).toBe("2024-06-01")
      expect(result.roadTypeKey).toBe("highway")
    })

    it("includes radius for cityCenter phase", () => {
      const geometry: GeoJSON.Point = {
        type: "Point",
        coordinates: [127.5, 36.0],
      }
      const modal = cityCenterModal({ radius: 500 })
      const result = buildFeatureData(geometry, makePhase("cityCenter"), modal) as FeatureData

      expect(result.radius).toBe(500)
    })
  })

  describe("toApiSaveShape", () => {
    it('maps "areas" to area type', () => {
      const result = toApiSaveShape({
        type: "areas",
        label: "A",
        decisionNumber: "",
        decisionDate: "",
        areaTypeKey: "central_urban",
      })
      expect(result).toEqual({ type: "area", layer: "central_urban" })
    })

    it('maps "areas" with default layer', () => {
      const result = toApiSaveShape({
        type: "areas",
        label: "A",
        decisionNumber: "",
        decisionDate: "",
      })
      expect(result).toEqual({ type: "area", layer: "central_urban" })
    })

    it('maps "cityCenter"', () => {
      const result = toApiSaveShape({
        type: "cityCenter",
        label: "C",
        decisionNumber: "",
        decisionDate: "",
      })
      expect(result).toEqual({ type: "city_center", layer: "city_center" })
    })

    it('maps "districts" with default layer', () => {
      const result = toApiSaveShape({
        type: "districts",
        label: "D",
        decisionNumber: "",
        decisionDate: "",
      })
      expect(result).toEqual({ type: "district", layer: "district" })
    })

    it('maps "roads" with default layer', () => {
      const result = toApiSaveShape({
        type: "roads",
        label: "R",
        decisionNumber: "",
        decisionDate: "",
        roadTypeKey: "highway",
      })
      expect(result).toEqual({ type: "road", layer: "highway" })
    })

    it('maps "houseEntrances" with entranceTypeKey', () => {
      const result = toApiSaveShape({
        type: "houseEntrances",
        label: "H",
        decisionNumber: "",
        decisionDate: "",
        entranceTypeKey: "main_entrance",
      })
      expect(result).toEqual({
        type: "house_entrance",
        layer: "main_entrance",
      })
    })

    it('maps "houseEntrances" with default layer', () => {
      const result = toApiSaveShape({
        type: "houseEntrances",
        label: "H",
        decisionNumber: "",
        decisionDate: "",
      })
      expect(result).toEqual({
        type: "house_entrance",
        layer: "main_entrance",
      })
    })

    it('maps "publicBuildings"', () => {
      const result = toApiSaveShape({
        type: "publicBuildings",
        label: "B",
        decisionNumber: "",
        decisionDate: "",
        buildingTypeKey: "school",
      })
      expect(result).toEqual({
        type: "public_building",
        layer: "school",
      })
    })

    it('maps "publicSpaces"', () => {
      const result = toApiSaveShape({
        type: "publicSpaces",
        label: "S",
        decisionNumber: "",
        decisionDate: "",
        spaceTypeKey: "park",
      })
      expect(result).toEqual({ type: "public_space", layer: "park" })
    })

    it('maps "namingPanels"', () => {
      const result = toApiSaveShape({
        type: "namingPanels",
        label: "N",
        decisionNumber: "",
        decisionDate: "",
      })
      expect(result).toEqual({ type: "naming_panel", layer: "naming_panel" })
    })
  })
})