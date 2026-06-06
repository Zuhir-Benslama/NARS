import { describe, it, expect, beforeEach } from "vitest"
import {
  pointInMunicipalLimit,
  pointInScatteredArea,
  renderScatteredAreas,
  computeCircleRing,
  computeCircleRingForEdit,
  closeRing,
  haversineDistance,
  computeCircleRadius,
  makeGeoJsonFeature,
} from "./geometry"

const TOLERANCE_M = 50

describe("geometry", () => {
  describe("pointInMunicipalLimit", () => {
    it("returns true when no boundary loaded", () => {
      expect(pointInMunicipalLimit(0, 0)).toBe(true)
    })
  })

  describe("renderScatteredAreas & pointInScatteredArea", () => {
    beforeEach(() => {
      renderScatteredAreas("")
    })

    it("parses a Polygon GeoJSON string", () => {
      const geojson = JSON.stringify({
        type: "Polygon",
        coordinates: [
          [
            [0, 0],
            [10, 0],
            [5, 10],
            [0, 0],
          ],
        ],
      })
      renderScatteredAreas(geojson)
      expect(pointInScatteredArea(5, 2)).toBe(true)
      expect(pointInScatteredArea(20, 20)).toBe(false)
    })

    it("parses a MultiPolygon GeoJSON string", () => {
      const geojson = JSON.stringify({
        type: "MultiPolygon",
        coordinates: [
          [
            [
              [0, 0],
              [10, 0],
              [5, 10],
              [0, 0],
            ],
          ],
        ],
      })
      renderScatteredAreas(geojson)
      expect(pointInScatteredArea(5, 2)).toBe(true)
    })

    it("rejects points inside holes", () => {
      const geojson = JSON.stringify({
        type: "Polygon",
        coordinates: [
          [
            [0, 0],
            [20, 0],
            [10, 20],
            [0, 0],
          ],
          [
            [5, 5],
            [15, 5],
            [10, 15],
            [5, 5],
          ],
        ],
      })
      renderScatteredAreas(geojson)
      expect(pointInScatteredArea(10, 1)).toBe(true)
      expect(pointInScatteredArea(10, 10)).toBe(false)
    })

    it("handles empty/null geometry string", () => {
      renderScatteredAreas("")
      expect(pointInScatteredArea(0, 0)).toBe(false)
    })

    it("handles invalid JSON gracefully", () => {
      renderScatteredAreas("not-json")
      expect(pointInScatteredArea(0, 0)).toBe(false)
    })

    it("handles Polygon with no type field gracefully", () => {
      renderScatteredAreas(JSON.stringify({}))
      expect(pointInScatteredArea(0, 0)).toBe(false)
    })

    it("accepts GeoJSON.Geometry object directly", () => {
      renderScatteredAreas({
        type: "Polygon",
        coordinates: [
          [
            [0, 0],
            [10, 0],
            [5, 10],
            [0, 0],
          ],
        ],
      })
      expect(pointInScatteredArea(5, 2)).toBe(true)
    })
  })

  describe("computeCircleRing", () => {
    it("returns a closed ring with 65 points (64 segments + close)", () => {
      const ring = computeCircleRing(0, 0, 1000)
      expect(ring.length).toBe(65)
    })

    it("first and last points match (closed ring)", () => {
      const ring = computeCircleRing(36.7, 3.2, 500)
      expect(ring[0][0]).toBe(ring[ring.length - 1][0])
      expect(ring[0][1]).toBe(ring[ring.length - 1][1])
    })

    it("points are roughly within expected radius", () => {
      const lat = 48.8566
      const lng = 2.3522
      const radius = 1000
      const ring = computeCircleRing(lat, lng, radius)
      for (let i = 0; i < ring.length - 1; i++) {
        const d = haversineDistance(lat, lng, ring[i][1], ring[i][0])
        expect(d).toBeGreaterThan(radius - TOLERANCE_M)
        expect(d).toBeLessThan(radius + TOLERANCE_M)
      }
    })

    it("zero radius produces points near center", () => {
      const ring = computeCircleRing(36.7, 3.2, 0)
      for (const [lng, lat] of ring) {
        const d = haversineDistance(36.7, 3.2, lat, lng)
        expect(d).toBeLessThan(1)
      }
    })
  })

  describe("computeCircleRingForEdit", () => {
    it("returns fewer segments than computeCircleRing", () => {
      const ring = computeCircleRingForEdit(36.7, 3.2, 500)
      expect(ring.length).toBe(17)
    })
  })

  describe("closeRing", () => {
    it("appends first point when ring is open", () => {
      const ring: [number, number][] = [
        [0, 0],
        [1, 0],
        [1, 1],
      ]
      const result = closeRing(ring)
      expect(result.length).toBe(4)
      expect(result[3][0]).toBe(0)
      expect(result[3][1]).toBe(0)
    })

    it("does not modify an already closed ring", () => {
      const ring: [number, number][] = [
        [0, 0],
        [1, 0],
        [1, 1],
        [0, 0],
      ]
      const result = closeRing(ring)
      expect(result.length).toBe(4)
    })

    it("returns empty array unchanged", () => {
      const ring: [number, number][] = []
      expect(closeRing(ring)).toEqual([])
    })

    it("returns single point ring unchanged", () => {
      const ring: [number, number][] = [[5, 5]]
      expect(closeRing(ring)).toEqual([[5, 5]])
    })
  })

  describe("haversineDistance", () => {
    it("zero distance for same point", () => {
      expect(haversineDistance(36.7, 3.2, 36.7, 3.2)).toBe(0)
    })

    it("approximate distance between known points", () => {
      const paris = { lat: 48.8566, lng: 2.3522 }
      const london = { lat: 51.5074, lng: -0.1278 }
      const d = haversineDistance(paris.lat, paris.lng, london.lat, london.lng)
      expect(d).toBeGreaterThan(300000)
      expect(d).toBeLessThan(400000)
    })

    it("symmetric", () => {
      const d1 = haversineDistance(0, 0, 10, 10)
      const d2 = haversineDistance(10, 10, 0, 0)
      expect(Math.abs(d1 - d2)).toBeLessThan(0.001)
    })

    it("1 degree lat ~ 111km", () => {
      const d = haversineDistance(0, 0, 1, 0)
      expect(d).toBeGreaterThan(110000)
      expect(d).toBeLessThan(112000)
    })
  })

  describe("computeCircleRadius", () => {
    it("returns average radius from ring points", () => {
      const lat = 36.7
      const lng = 3.2
      const radius = 500
      const ring = computeCircleRing(lat, lng, radius)
      const avgRadius = computeCircleRadius(lat, lng, ring)
      expect(avgRadius).toBeGreaterThan(radius - TOLERANCE_M)
      expect(avgRadius).toBeLessThan(radius + TOLERANCE_M)
    })

    it("returns 0 for empty ring", () => {
      expect(computeCircleRadius(0, 0, [])).toBe(0)
    })
  })

  describe("makeGeoJsonFeature", () => {
    it("creates a Feature with given geometry and properties", () => {
      const geometry: GeoJSON.Point = { type: "Point", coordinates: [3.2, 36.7] }
      const properties = { label: "test", id: 1 }
      const feature = makeGeoJsonFeature(geometry, properties)
      expect(feature.type).toBe("Feature")
      expect(feature.geometry).toEqual(geometry)
      expect(feature.properties).toEqual(properties)
    })
  })
})
