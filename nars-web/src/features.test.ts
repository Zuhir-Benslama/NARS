// ─── FEATURES TESTS ───────────────────────────────────────────────────────────
// Tests for map/features.ts functions.

import { describe, it, expect } from "vitest"
import { buildFeatureData, toApiSaveShape } from "./map/features/feature-data"
import type { FeatureData } from "./types"
import { PHASES } from "./phases"

describe("buildFeatureData", () => {
  const baseModalResult = {
    label: "Test Feature",
    decisionNumber: "2024/001",
    decisionDate: "2024-01-15",
  }

  const areaPhase = PHASES.find((p) => p.key === "areas")!

  it("builds FeatureData for Point geometry", () => {
    const pointGeometry: GeoJSON.Point = {
      type: "Point",
      coordinates: [3.058, 36.753],
    }

    const result = buildFeatureData(pointGeometry, areaPhase, {
      type: "areas",
      ...baseModalResult,
      areaTypeKey: "central_urban",
    }) as FeatureData

    expect(result.type).toBe("areas")
    expect(result.label).toBe("Test Feature")
    expect(result.lat).toBe(36.753)
    expect(result.lng).toBe(3.058)
    expect(result.coordinates).toEqual([{ lat: 36.753, lng: 3.058 }])
  })

  it("builds FeatureData for LineString geometry", () => {
    const lineGeometry: GeoJSON.LineString = {
      type: "LineString",
      coordinates: [
        [3.058, 36.753],
        [3.059, 36.754],
        [3.06, 36.755],
      ],
    }

    const roadPhase = PHASES.find((p) => p.key === "roads")!

    const result = buildFeatureData(lineGeometry, roadPhase, {
      type: "roads",
      ...baseModalResult,
      roadTypeKey: "street",
    })

    expect(result.type).toBe("roads")
    expect(result.coordinates).toHaveLength(3)
    expect(result.coordinates?.[0]).toEqual({ lat: 36.753, lng: 3.058 })
    expect(result.coordinates?.[2]).toEqual({ lat: 36.755, lng: 3.06 })
  })

  it("builds FeatureData for Polygon geometry", () => {
    const polygonGeometry: GeoJSON.Polygon = {
      type: "Polygon",
      coordinates: [
        [
          [3.058, 36.753],
          [3.059, 36.754],
          [3.06, 36.755],
          [3.058, 36.753],
        ],
      ],
    }

    const result = buildFeatureData(polygonGeometry, areaPhase, {
      type: "areas",
      ...baseModalResult,
      areaTypeKey: "secondary_urban",
    })

    expect(result.type).toBe("areas")
    expect(result.coordinates).toHaveLength(4)
    expect(result.coordinates?.[0]).toEqual({ lat: 36.753, lng: 3.058 })
  })

  it("preserves all modal result fields", () => {
    const pointGeometry: GeoJSON.Point = {
      type: "Point",
      coordinates: [3.058, 36.753],
    }

    const entrancePhase = PHASES.find((p) => p.key === "houseEntrances")!

    const result = buildFeatureData(pointGeometry, entrancePhase, {
      type: "houseEntrances",
      label: "Test Feature",
      entranceTypeKey: "main_entrance",
      roadDbId: "test-uuid-42",
      roadLabel: "Main Street",
      side: "left" as const,
      entranceNumber: 5,
    }) as FeatureData

    expect(result.entranceTypeKey).toBe("main_entrance")
    expect(result.roadDbId).toBe("test-uuid-42")
    expect(result.roadLabel).toBe("Main Street")
    expect(result.side).toBe("left")
    expect(result.entranceNumber).toBe(5)
  })

  it("removes undefined fields from result", () => {
    const pointGeometry: GeoJSON.Point = {
      type: "Point",
      coordinates: [3.058, 36.753],
    }

    const result = buildFeatureData(pointGeometry, areaPhase, {
      type: "areas",
      ...baseModalResult,
    }) as FeatureData

    expect(result.roadDbId).toBeUndefined()
    expect(result.entranceTypeKey).toBeUndefined()
    expect(result.spaceTypeKey).toBeUndefined()
  })
})

describe("toApiSaveShape", () => {
  it("maps areas to correct API shape", () => {
    const result = toApiSaveShape({
      type: "areas",
      label: "Test",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
      areaTypeKey: "central_urban",
    })

    expect(result).toEqual({ type: "area", layer: "central_urban" })
  })

  it("maps cityCenter to correct API shape", () => {
    const result = toApiSaveShape({
      type: "cityCenter",
      label: "City Center",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
    })

    expect(result).toEqual({ type: "city_center", layer: "city_center" })
  })

  it("maps districts to correct API shape", () => {
    const result = toApiSaveShape({
      type: "districts",
      label: "District",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
      districtTypeKey: "housing_estate",
    })

    expect(result).toEqual({ type: "district", layer: "housing_estate" })
  })

  it("maps roads to correct API shape", () => {
    const result = toApiSaveShape({
      type: "roads",
      label: "Street",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
      roadTypeKey: "avenue",
    })

    expect(result).toEqual({ type: "road", layer: "avenue" })
  })

  it("maps houseEntrances to correct API shape", () => {
    const result = toApiSaveShape({
      type: "houseEntrances",
      label: "Entrance",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
      entranceTypeKey: "main_entrance",
    })

    expect(result).toEqual({ type: "house_entrance", layer: "main_entrance" })
  })

  it("maps publicBuildings to correct API shape", () => {
    const result = toApiSaveShape({
      type: "publicBuildings",
      label: "School",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
      buildingTypeKey: "school",
    })

    expect(result).toEqual({ type: "public_building", layer: "school" })
  })

  it("maps publicSpaces to correct API shape", () => {
    const result = toApiSaveShape({
      type: "publicSpaces",
      label: "Garden",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
      spaceTypeKey: "garden",
    })

    expect(result).toEqual({ type: "public_space", layer: "garden" })
  })

  it("uses default layer when type key is missing", () => {
    const result = toApiSaveShape({
      type: "areas",
      label: "Test",
      decisionNumber: "001",
      decisionDate: "2024-01-01",
    })

    expect(result).toEqual({ type: "area", layer: "central_urban" })
  })
})
