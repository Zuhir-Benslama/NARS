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
      orientFromCityCenter({ lat: 36.0, lng: 127.0 }, 1, graph, segs, visited)

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
  })
})
