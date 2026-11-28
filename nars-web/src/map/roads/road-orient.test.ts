import { describe, it, expect } from "vitest"
import Graph from "graphology"
import type { Coord, Seg } from "./road-graph"
import { nk } from "./road-graph"

import { geographicDirection, orientFromRoundabouts } from "./road-orient"

function makeSeg(coords: Coord[], dbId = "db-1", roadTypeKey = "street"): Seg {
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
        roadTypeKey,
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

  describe("orientFromRoundabouts", () => {
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
      orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 1100, graph, segs, visited)

      expect(visited.size).toBeGreaterThan(0)
    })

    it("handles empty graph without error", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()
      const visited = new Set<string>()

      expect(() =>
        orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 100, graph, segs, visited),
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
      orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 1000, graph, segs, visited)

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
      orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 1000, graph, segs, visited)

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
      orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 100000, graph, segs, visited)

      expect(visited.size).toBe(0)
      expect(seg.reversed).toBe(false)
    })

    it("walks primary edges before closer tertiary edges when category priority is enabled", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()

      const a = nk({ lat: 36.009, lng: 127.0 }) // seed on the 1000 m ring
      const near = nk({ lat: 36.01, lng: 127.0 }) // ~1113 m — tertiary road
      const far = nk({ lat: 36.02, lng: 127.0 }) // ~2226 m — primary road
      graph.addNode(a)
      graph.addNode(near)
      graph.addNode(far)
      graph.addEdgeWithKey("near-tertiary", a, near)
      graph.addEdgeWithKey("far-primary", a, far)

      segs.set(
        "near-tertiary",
        makeSeg(
          [
            { lat: 36.009, lng: 127.0 },
            { lat: 36.01, lng: 127.0 },
          ],
          "near",
          "drive",
        ),
      )
      segs.set(
        "far-primary",
        makeSeg(
          [
            { lat: 36.009, lng: 127.0 },
            { lat: 36.02, lng: 127.0 },
          ],
          "far",
          "boulevard",
        ),
      )

      const visited = new Set<string>()
      orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 1000, graph, segs, visited, true)

      expect([...visited][0]).toBe("far-primary")
      expect(visited.has("near-tertiary")).toBe(true)
    })

    it("walks primary seeds before tertiary seeds only when category priority is enabled", () => {
      const run = (prioritizeCategory: boolean): string[] => {
        const graph = new Graph({ multi: true, type: "undirected" })
        const segs = new Map<string, Seg>()

        // Both seeds sit on the 1000 m ring; the tertiary far endpoint is closer.
        const tertiarySeed = nk({ lat: 36.009, lng: 127.00001 })
        const primarySeed = nk({ lat: 36.0092, lng: 127.0 })
        const tertiaryEnd = nk({ lat: 36.018, lng: 127.00001 })
        const primaryEnd = nk({ lat: 36.02, lng: 127.0 })
        graph.addNode(tertiarySeed)
        graph.addNode(primarySeed)
        graph.addNode(tertiaryEnd)
        graph.addNode(primaryEnd)
        graph.addEdgeWithKey("tertiary-edge", tertiarySeed, tertiaryEnd)
        graph.addEdgeWithKey("primary-edge", primarySeed, primaryEnd)

        segs.set(
          "tertiary-edge",
          makeSeg(
            [
              { lat: 36.009, lng: 127.00001 },
              { lat: 36.018, lng: 127.00001 },
            ],
            "tertiary-road",
            "lane",
          ),
        )
        segs.set(
          "primary-edge",
          makeSeg(
            [
              { lat: 36.0092, lng: 127.0 },
              { lat: 36.02, lng: 127.0 },
            ],
            "primary-road",
            "avenue",
          ),
        )

        const visited = new Set<string>()
        orientFromRoundabouts(
          { lat: 36.0, lng: 127.0 },
          1000,
          graph,
          segs,
          visited,
          prioritizeCategory,
        )
        return [...visited]
      }

      expect(run(false)[0]).toBe("tertiary-edge")
      expect(run(true)[0]).toBe("primary-edge")
    })

    it("serves the closest wave first instead of diving down one branch", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()

      const a = nk({ lat: 36.009, lng: 127.0 }) // seed
      const b = nk({ lat: 36.01, lng: 127.0 }) // closest
      const d = nk({ lat: 36.012, lng: 127.0 }) // middle
      const c = nk({ lat: 36.015, lng: 127.0 }) // far end of the deep branch
      for (const node of [a, b, d, c]) graph.addNode(node)
      graph.addEdgeWithKey("near-edge", a, b)
      graph.addEdgeWithKey("mid-edge", a, d)
      graph.addEdgeWithKey("deep-edge", b, c)

      segs.set(
        "near-edge",
        makeSeg(
          [
            { lat: 36.009, lng: 127.0 },
            { lat: 36.01, lng: 127.0 },
          ],
          "near-road",
        ),
      )
      segs.set(
        "mid-edge",
        makeSeg(
          [
            { lat: 36.009, lng: 127.0 },
            { lat: 36.012, lng: 127.0 },
          ],
          "mid-road",
        ),
      )
      segs.set(
        "deep-edge",
        makeSeg(
          [
            { lat: 36.01, lng: 127.0 },
            { lat: 36.015, lng: 127.0 },
          ],
          "deep-road",
        ),
      )

      const visited = new Set<string>()
      orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 1000, graph, segs, visited)

      expect([...visited]).toEqual(["near-edge", "mid-edge", "deep-edge"])
    })

    it("orders by accumulated road distance rather than straight-line distance", () => {
      const graph = new Graph({ multi: true, type: "undirected" })
      const segs = new Map<string, Seg>()

      const a = nk({ lat: 36.009, lng: 127.0 }) // seed ~1000 m from the centre
      const detourEnd = nk({ lat: 36.0095, lng: 127.02 }) // closer straight-line, longer road
      const shortcutEnd = nk({ lat: 36.02, lng: 127.0 }) // farther straight-line, shorter road
      for (const node of [a, detourEnd, shortcutEnd]) graph.addNode(node)
      graph.addEdgeWithKey("detour-edge", a, detourEnd)
      graph.addEdgeWithKey("shortcut-edge", a, shortcutEnd)

      segs.set(
        "detour-edge",
        makeSeg(
          [
            { lat: 36.009, lng: 127.0 },
            { lat: 36.0095, lng: 127.02 },
          ],
          "detour-road",
        ),
      )
      segs.set(
        "shortcut-edge",
        makeSeg(
          [
            { lat: 36.009, lng: 127.0 },
            { lat: 36.02, lng: 127.0 },
          ],
          "shortcut-road",
        ),
      )

      const visited = new Set<string>()
      orientFromRoundabouts({ lat: 36.0, lng: 127.0 }, 1000, graph, segs, visited)

      expect([...visited][0]).toBe("shortcut-edge")
    })
  })
})
