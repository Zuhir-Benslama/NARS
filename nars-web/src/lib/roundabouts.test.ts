import { describe, it, expect } from "vitest"
import type { FeatureData, LatLng } from "../types"
import enMessages from "../i18n/en.json"
import frMessages from "../i18n/fr.json"
import arMessages from "../i18n/ar.json"
import {
  roundaboutsPointFromGeometry,
  roundaboutsRadiusError,
  roundaboutsNames,
  roundaboutsSubtype,
  plainRoundaboutsPlacementError,
} from "./roundabouts"

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

function roundabouts(id: string, lat?: number, lng?: number, subtype = "city_center") {
  return {
    dbId: id,
    data: {
      type: "roundabouts",
      label: id,
      ...(lat != null && lng != null ? { lat, lng } : {}),
      roundaboutsTypeKey: subtype,
    } as FeatureData,
  }
}

describe("roundaboutsSubtype", () => {
  it("reads the current JSON key", () => {
    expect(roundaboutsSubtype({ roundaboutsTypeKey: "roundabout" })).toBe("roundabout")
  })

  it("falls back to the legacy JSON key", () => {
    expect(roundaboutsSubtype({ cityCenterTypeKey: "roundabout" })).toBe("roundabout")
  })

  it("defaults to the plain city-center subtype", () => {
    expect(roundaboutsSubtype({})).toBe("city_center")
    expect(roundaboutsSubtype(null)).toBe("city_center")
  })
})

describe("roundaboutsRadiusError", () => {
  it("returns too_small for null/undefined/NaN radius", () => {
    expect(roundaboutsRadiusError(null)).toBe("too_small")
    expect(roundaboutsRadiusError(undefined)).toBe("too_small")
    expect(roundaboutsRadiusError(Number.NaN)).toBe("too_small")
  })

  it("returns too_small below the minimum radius", () => {
    expect(roundaboutsRadiusError(0)).toBe("too_small")
    expect(roundaboutsRadiusError(4.9)).toBe("too_small")
    expect(roundaboutsRadiusError(5)).toBeNull()
  })

  it("returns too_large above the maximum radius", () => {
    expect(roundaboutsRadiusError(50_000.1)).toBe("too_large")
  })

  it("returns null for a valid radius", () => {
    expect(roundaboutsRadiusError(10)).toBeNull()
    expect(roundaboutsRadiusError(50_000)).toBeNull()
  })
})

describe("plainRoundaboutsPlacementError", () => {
  const main = area("main", 0, 0)
  const secondary = area("secondary", 10, 10)

  it("allows one plain city center in an urban area", () => {
    expect(plainRoundaboutsPlacementError({ lat: 0, lng: 0 }, [main], [])).toBeNull()
  })

  it("rejects a point outside every urban area", () => {
    expect(plainRoundaboutsPlacementError({ lat: 5, lng: 5 }, [main], [])).toBe(
      "outside_urban_area",
    )
  })

  it("rejects a second plain city center in the same urban area", () => {
    expect(
      plainRoundaboutsPlacementError(
        { lat: 0.5, lng: 0.5 },
        [main],
        [roundabouts("existing", 0, 0)],
      ),
    ).toBe("urban_area_has_plain_roundabouts")
  })

  it("allows one plain city center in each urban area", () => {
    expect(
      plainRoundaboutsPlacementError(
        { lat: 10, lng: 10 },
        [main, secondary],
        [roundabouts("existing", 0, 0)],
      ),
    ).toBeNull()
  })

  it("does not count roundabouts toward the slot", () => {
    expect(
      plainRoundaboutsPlacementError(
        { lat: 0, lng: 0 },
        [main],
        [roundabouts("roundabout", 0, 0, "roundabout")],
      ),
    ).toBeNull()
  })

  it("ignores the feature currently being edited", () => {
    expect(
      plainRoundaboutsPlacementError(
        { lat: 0.5, lng: 0.5 },
        [main],
        [roundabouts("self", 0, 0)],
        "self",
      ),
    ).toBeNull()
  })
})

describe("roundaboutsPointFromGeometry", () => {
  it("reads a point's coordinates", () => {
    expect(roundaboutsPointFromGeometry({ type: "Point", coordinates: [2, 1] })).toEqual({
      lat: 1,
      lng: 2,
    })
  })

  it("averages a polygon ring without double-counting the closing vertex", () => {
    const point = roundaboutsPointFromGeometry({
      type: "Polygon",
      coordinates: [
        [
          [0, 0],
          [2, 0],
          [2, 2],
          [0, 2],
          [0, 0],
        ],
      ],
    })
    expect(point).toEqual({ lat: 1, lng: 1 })
  })
})

describe("roundaboutsNames", () => {
  it.each(["city_center", "roundabout"] as const)(
    "%s matches the translated feature type labels",
    (subtypeKey) => {
      const names = roundaboutsNames(subtypeKey)

      expect(names).toEqual({
        label: enMessages.featureTypes[subtypeKey],
        labelEn: enMessages.featureTypes[subtypeKey],
        labelFr: frMessages.featureTypes[subtypeKey],
        labelAr: arMessages.featureTypes[subtypeKey],
      })
    },
  )

  it("falls back to the city center name for an unknown subtype", () => {
    expect(roundaboutsNames("unknown")).toEqual(roundaboutsNames("city_center"))
  })
})
