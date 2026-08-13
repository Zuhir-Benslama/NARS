import { describe, it, expect } from "vitest"
import Graph from "graphology"
import type { Coord, Seg } from "./road-graph"
import { nk } from "./road-graph"

import { geographicDirection, orientFromCityCenter } from "./road-orient"

function makeSeg(coords: Coord[], dbId = "db-1"): Seg {
  return {
    coords,
    entry: {
      id: "r1",
      dbId,
      type: "line" as const,
      data: {
        type: "roads" as const,
        label: "",
        decisionNumber: "",
        decisionDate: "",
        roadTypeKey: "street",
        coordinates: coords,
      },
    },
    dbId,
    reversed: false,
  }
}

describe("road-orient", () => {
  describe("geographicDirection", () => {
    it("sets reversed=true when start lat < end lat (N→S)", () => {
      const seg = makeSeg([
        { lat: 36.0, lng: 127.0 },
        { lat: 36.02, lng: 127.0 },
      ])
      geographicDirection(seg)
      expect(seg.reversed).toBe(true)
    })

    it("sets reversed=false when start lat > end lat (S→N)", () => {
      const seg = makeSeg([
        { lat: 36.02, lng: 127.0 },
        { lat: 36.0, lng: 127.0 },
      ])
      geographicDirection(seg)
      expect(seg.reversed).toBe(false)
    })

    it("uses lng heuristic when dLat < dLng", () => {
      const seg = makeSeg([
        { lat: 36.0, lng: 127.0 },
        { lat: 36.0, lng: 127.02 },
      ])
      geographicDirection(seg)
      expect(seg.reversed).toBe(true)
    })

    it("sets reversed=false when lng decreases", () => {
      const seg = makeSeg([
        { lat: 36.0, lng: 127.02 },
        { lat: 36.0, lng: 127.0 },
      ])
      geographicDirection(seg)
      expect(seg.reversed).toBe(false)
    })

    it("handles equal lat/lng distance", () => {
      const seg = makeSeg([
        { lat: 36.0, lng: 127.0 },
        { lat: 36.001, lng: 127.001 },
      ])
      geographicDirection(seg)
      expect(typeof seg.reversed).toBe("boolean")
    })
  })

  describe("orientFromCityCenter", () => {
    it("processes graph without error", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()

      const a = nk({ lat: 36.0, lng: 127.0 })
      const b = nk({ lat: 36.01, lng: 127.0 })
      graph.addNode(a)
      graph.addNode(b)

      const seg = makeSeg([
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.0 },
      ])
      graph.addEdgeWithKey("seg1", a, b)
      segs.set("seg1", seg)

      const visited = new Set<string>()
      // Node `b` sits ~1113 m from the center — a 1100 m ring captures it within
      // the 30 m tolerance.
      orientFromCityCenter({ lat: 36.0, lng: 127.0 }, 1100, graph, segs, visited)

      expect(visited.size).toBeGreaterThan(0)
    })

    it("handles empty graph without error", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()
      const visited = new Set<string>()

      expect(() =>
        orientFromCityCenter({ lat: 36.0, lng: 127.0 }, 100, graph, segs, visited),
      ).not.toThrow()
    })

    it("orients a multi-segment road outward from the seed and marks every edge visited", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()

      // A sits ~1km from the center (seed on the 1000 m ring); B and C are farther out.
      const a = nk({ lat: 36.009, lng: 127.0 })
      const b = nk({ lat: 36.02, lng: 127.0 })
      const c = nk({ lat: 36.03, lng: 127.0 })
      graph.addNode(a)
      graph.addNode(b)
      graph.addNode(c)

      // seg1 stored B→A so it must be reversed to orient A→B.
      const seg1 = makeSeg(
        [
          { lat: 36.02, lng: 127.0 },
          { lat: 36.009, lng: 127.0 },
        ],
        "road-1",
      )
      const seg2 = makeSeg(
        [
          { lat: 36.02, lng: 127.0 },
          { lat: 36.03, lng: 127.0 },
        ],
        "road-1",
      )
      graph.addEdgeWithKey("seg1", a, b)
      graph.addEdgeWithKey("seg2", b, c)
      segs.set("seg1", seg1)
      segs.set("seg2", seg2)

      const visited = new Set<string>()
      orientFromCityCenter({ lat: 36.0, lng: 127.0 }, 1000, graph, segs, visited)

      expect(seg1.reversed).toBe(true)
      expect(seg2.reversed).toBe(false)
      expect(visited.has("seg1")).toBe(true)
      expect(visited.has("seg2")).toBe(true)
    })

    it("ignores graph edges that have no matching segment", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()

      const a = nk({ lat: 36.009, lng: 127.0 })
      const b = nk({ lat: 36.02, lng: 127.0 })
      graph.addNode(a)
      graph.addNode(b)

      const seg = makeSeg([
        { lat: 36.009, lng: 127.0 },
        { lat: 36.02, lng: 127.0 },
      ])
      graph.addEdgeWithKey("seg1", a, b)
      graph.addEdgeWithKey("ghost", a, b) // not present in segs map
      segs.set("seg1", seg)

      const visited = new Set<string>()
      orientFromCityCenter({ lat: 36.0, lng: 127.0 }, 1000, graph, segs, visited)

      expect(visited.has("ghost")).toBe(false)
      expect(visited.has("seg1")).toBe(true)
    })

    it("skips seedless graphs when no node is near the radius", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()

      const a = nk({ lat: 36.009, lng: 127.0 })
      const b = nk({ lat: 36.02, lng: 127.0 })
      graph.addNode(a)
      graph.addNode(b)

      const seg = makeSeg([
        { lat: 36.009, lng: 127.0 },
        { lat: 36.02, lng: 127.0 },
      ])
      graph.addEdgeWithKey("seg1", a, b)
      segs.set("seg1", seg)

      const visited = new Set<string>()
      // radius far larger than any node distance → no seeds
      orientFromCityCenter({ lat: 36.0, lng: 127.0 }, 100000, graph, segs, visited)

      expect(visited.size).toBe(0)
      expect(seg.reversed).toBe(false)
    })
  })
})
