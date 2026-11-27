import { describe, it, expect } from "vitest"
import type { FeatureData, LatLng } from "../types"
import { cityCenterRadiusError, plainCityCenterPlacementError } from "./city-center"

function square(centerLat: number, centerLng: number): LatLng[] {
  return [
    { lat: centerLat - 1, lng: centerLng - 1 },
    { lat: centerLat - 1, lng: centerLng + 1 },
    { lat: centerLat + 1, lng: centerLng + 1 },
    { lat: centerLat + 1, lng: centerLng - 1 },
    { lat: centerLat - 1, lng: centerLng - 1 },
  ]
}

function area(id: string, centerLat: number, centerLng: number) {
  return {
    dbId: id,
    data: {
      type: "areas",
      label: id,
      areaTypeKey: id === "main" ? "central_urban" : "secondary_urban",
      coordinates: square(centerLat, centerLng),
    } as FeatureData,
  }
}

function cityCenter(id: string, lat?: number, lng?: number, subtype = "city_center") {
  return {
    dbId: id,
    data: {
      type: "cityCenter",
      label: id,
      ...(lat != null && lng != null ? { lat, lng } : {}),
      cityCenterTypeKey: subtype,
    } as FeatureData,
  }
}

describe("cityCenterRadiusError", () => {
  it("returns too_small for null/undefined/NaN radius", () => {
    expect(cityCenterRadiusError(null)).toBe("too_small")
    expect(cityCenterRadiusError(undefined)).toBe("too_small")
    expect(cityCenterRadiusError(Number.NaN)).toBe("too_small")
  })

  it("returns too_small below the minimum radius", () => {
    expect(cityCenterRadiusError(0)).toBe("too_small")
    expect(cityCenterRadiusError(4.9)).toBe("too_small")
    expect(cityCenterRadiusError(5)).toBeNull()
  })

  it("returns too_large above the maximum radius", () => {
    expect(cityCenterRadiusError(50_000.1)).toBe("too_large")
  })

  it("returns null for a valid radius", () => {
    expect(cityCenterRadiusError(10)).toBeNull()
    expect(cityCenterRadiusError(50_000)).toBeNull()
  })
})

describe("plainCityCenterPlacementError", () => {
  const main = area("main", 0, 0)
  const secondary = area("secondary", 10, 10)

  it("allows one plain city center in an urban area", () => {
    expect(plainCityCenterPlacementError({ lat: 0, lng: 0 }, [main], [])).toBeNull()
  })

  it("rejects a point outside every urban area", () => {
    expect(plainCityCenterPlacementError({ lat: 5, lng: 5 }, [main], [])).toBe("outside_urban_area")
  })

  it("rejects a second plain city center in the same urban area", () => {
    expect(
      plainCityCenterPlacementError({ lat: 0.5, lng: 0.5 }, [main], [cityCenter("existing", 0, 0)]),
    ).toBe("urban_area_has_city_center")
  })

  it("allows one plain city center in each urban area", () => {
    expect(
      plainCityCenterPlacementError(
        { lat: 10, lng: 10 },
        [main, secondary],
        [cityCenter("existing", 0, 0)],
      ),
    ).toBeNull()
  })

  it("does not count roundabouts toward the slot", () => {
    expect(
      plainCityCenterPlacementError(
        { lat: 0, lng: 0 },
        [main],
        [cityCenter("roundabout", 0, 0, "roundabout")],
      ),
    ).toBeNull()
  })

  it("ignores the feature currently being edited", () => {
    expect(
      plainCityCenterPlacementError(
        { lat: 0.5, lng: 0.5 },
        [main],
        [cityCenter("self", 0, 0)],
        "self",
      ),
    ).toBeNull()
  })
})
