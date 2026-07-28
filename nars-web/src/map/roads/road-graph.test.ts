import { describe, it, expect } from "vitest"
import { buildConnectionGraph, nk, fromNk, dm, toPt, toLn } from "./road-graph"
import type { LayerEntry } from "../../types"

function makeRoad(id: string, dbId: string, coords: { lat: number; lng: number }[]): LayerEntry {
  return {
    id,
    dbId,
    type: "line",
    data: {
      type: "roads",
      label: "",
      decisionNumber: "",
      decisionDate: "",
      roadTypeKey: "street",
      coordinates: coords,
    },
  }
}

describe("road-graph", () => {
  describe("nk / fromNk", () => {
    it("nk produces a string key from coordinates", () => {
      expect(nk({ lat: 36.12345, lng: 127.6789 })).toBe("36.12345,127.67890")
    })

    it("fromNk decodes a key back to coordinates", () => {
      const result = fromNk("36.12345,127.67890")
      expect(result.lat).toBeCloseTo(36.12345, 5)
      expect(result.lng).toBeCloseTo(127.6789, 5)
    })

    it("nk rounds to 5 decimal places", () => {
      expect(nk({ lat: 1.123456, lng: 2.654321 })).toBe("1.12346,2.65432")
    })
  })

  describe("toPt / toLn / dm", () => {
    it("toPt creates a Point feature", () => {
      const pt = toPt({ lat: 10, lng: 20 })
      expect(pt.geometry.coordinates).toEqual([20, 10])
    })

    it("toLn creates a LineString feature", () => {
      const ln = toLn([
        { lat: 0, lng: 0 },
        { lat: 1, lng: 1 },
      ])
      expect(ln.geometry.coordinates).toEqual([
        [0, 0],
        [1, 1],
      ])
    })

    it("dm computes distance between two points", () => {
      const d = dm({ lat: 36.0, lng: 127.0 }, { lat: 36.001, lng: 127.0 })
      expect(d).toBeGreaterThan(0)
      expect(d).toBeLessThan(200)
    })
  })

  describe("buildConnectionGraph", () => {
    it("returns empty graph and segs when roads array is empty", () => {
      const { graph, segs } = buildConnectionGraph([])
      expect(graph.order).toBe(0)
      expect(graph.size).toBe(0)
      expect(segs.size).toBe(0)
    })

    it("creates one edge for a single road", () => {
      const road = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])
      const { graph, segs } = buildConnectionGraph([road])
      expect(graph.order).toBe(2)
      expect(graph.size).toBe(1)
      expect(segs.size).toBe(1)
    })

    it("skips roads with no coordinates", () => {
      const road = makeRoad("r1", "db-1", [])
      const { graph, segs } = buildConnectionGraph([road])
      expect(graph.order).toBe(0)
      expect(segs.size).toBe(0)
    })

    it("deduplicates nearby nodes within CONNECT_M (30m)", () => {
      const road = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])
      const road2 = makeRoad("r2", "db-2", [
        { lat: 36.0001, lng: 127.0001 },
        { lat: 36.02, lng: 127.02 },
      ])
      const { graph, segs } = buildConnectionGraph([road, road2])
      // road[0] and road2[0] are very close → same node
      expect(graph.order).toBe(3)
      expect(graph.size).toBe(2)
      expect(segs.size).toBe(2)
    })

    it("creates T-junction when endpoint lands on body of another road", () => {
      const road1 = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.0, lng: 127.01 },
        { lat: 36.0, lng: 127.02 },
      ])
      const road2 = makeRoad("r2", "db-2", [
        { lat: 35.995, lng: 127.01 },
        { lat: 36.005, lng: 127.01 },
      ])
      const { graph, segs } = buildConnectionGraph([road1, road2])
      expect(graph.order).toBeGreaterThanOrEqual(2)
      expect(segs.size).toBeGreaterThanOrEqual(2)
    })

    it("skips endpoints that match road endpoints (not a T-junction)", () => {
      const road1 = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])
      const road2 = makeRoad("r2", "db-2", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.02, lng: 127.02 },
      ])
      const { graph } = buildConnectionGraph([road1, road2])
      expect(graph.size).toBe(2)
    })

    it("handles a road with no junctions as a single segment", () => {
      const road = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.0 },
        { lat: 36.02, lng: 127.0 },
      ])
      const { segs } = buildConnectionGraph([road])
      expect(segs.size).toBe(1)
      const seg = segs.values().next().value!
      expect(seg.coords.length).toBe(3)
    })

    it("sets reversed=false on all new segments", () => {
      const road = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])
      const { segs } = buildConnectionGraph([road])
      for (const seg of segs.values()) {
        expect(seg.reversed).toBe(false)
      }
    })

    it("assigns correct dbId and entry on segments", () => {
      const road = makeRoad("r1", "db-road-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])
      const { segs } = buildConnectionGraph([road])
      const seg = segs.values().next().value!
      expect(seg.dbId).toBe("db-road-1")
      expect(seg.entry).toBe(road)
    })
  })
})
