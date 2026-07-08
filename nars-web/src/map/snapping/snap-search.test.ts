import { describe, it, expect, vi, beforeEach } from "vitest"
import type maplibregl from "maplibre-gl"

const mockGetBounds = vi.fn()
const mockProjectFn = vi.fn()
const mockUnprojectFn = vi.fn()

vi.mock("../core/state", () => ({
  ctx: {
    map: {
      project: mockProjectFn,
      unproject: mockUnprojectFn,
      getBounds: mockGetBounds,
    },
    geoman: undefined,
  },
}))

const mockGetSnapRings = vi.fn()
const mockGetRoadChains = vi.fn()
const mockGetCityCenterCircles = vi.fn()
const mockGetSnapPoints = vi.fn()

vi.mock("./snap-sources", () => ({
  getSnapRings: mockGetSnapRings,
  getRoadChains: mockGetRoadChains,
  getCityCenterCircles: mockGetCityCenterCircles,
  getSnapPoints: mockGetSnapPoints,
}))

const project = (ll: [number, number]) => ({ x: ll[0], y: -ll[1] })
const unproject = (pt: [number, number]) =>
  ({ lng: pt[0], lat: -pt[1] }) as unknown as maplibregl.LngLat

let findNearestSnap: (
  cursorX: number,
  cursorY: number,
  phaseKeys: string[],
  includeMidpoint: boolean,
  excludeId?: string | null,
) => { lat: number; lng: number; type: string } | null
let mergeExternalSnapWithDrawFirstVertex: (
  cursorX: number,
  cursorY: number,
  external: { lat: number; lng: number; type: string } | null,
  project: (ll: [number, number]) => { x: number; y: number },
) => { lat: number; lng: number; type: string } | null
async function loadModule() {
  const mod = await import("./snap-search")
  findNearestSnap = mod.findNearestSnap
  mergeExternalSnapWithDrawFirstVertex = mod.mergeExternalSnapWithDrawFirstVertex
}

beforeEach(async () => {
  vi.clearAllMocks()
  const ctxMod = await import("../core/state")
  ctxMod.ctx.geoman = undefined
  mockProjectFn.mockImplementation((ll: [number, number]) => project(ll))
  mockUnprojectFn.mockImplementation((pt: [number, number]) => unproject(pt))
  mockGetBounds.mockReturnValue({
    getSouth: () => -90,
    getNorth: () => 90,
    getWest: () => -180,
    getEast: () => 180,
  })
  await loadModule()
})

describe("snap-search", () => {
  describe("mergeExternalSnapWithDrawFirstVertex", () => {
    function makeExternal(overrides = {}): { lat: number; lng: number; type: string } {
      return { lat: 30, lng: 120, type: "vertex", ...overrides }
    }

    it("returns external when no actionInstances", () => {
      const result = mergeExternalSnapWithDrawFirstVertex(0, 0, makeExternal(), project)
      expect(result).toEqual(makeExternal())
    })

    it("returns external when no lineDrawer", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = { actionInstances: { draw__polygon: {} } } as any

      const result = mergeExternalSnapWithDrawFirstVertex(0, 0, makeExternal(), project)
      expect(result).toEqual(makeExternal())
    })

    it("returns external when no shapeLngLats", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = {
        actionInstances: {
          draw__polygon: { lineDrawer: {} },
        },
      } as any

      const result = mergeExternalSnapWithDrawFirstVertex(0, 0, makeExternal(), project)
      expect(result).toEqual(makeExternal())
    })

    it("returns first vertex when it is close and no external", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = {
        actionInstances: {
          draw__polygon: {
            lineDrawer: { shapeLngLats: [[100, 0]] },
          },
        },
      } as any

      const result = mergeExternalSnapWithDrawFirstVertex(100, 0, null, project)
      expect(result).toEqual({ lng: 100, lat: 0, type: "vertex" })
    })

    it("returns first vertex when closer than external", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = {
        actionInstances: {
          draw__polygon: {
            lineDrawer: { shapeLngLats: [[100, 0]] },
          },
        },
      } as any

      const result = mergeExternalSnapWithDrawFirstVertex(
        100,
        0,
        { lng: 200, lat: 0, type: "vertex" },
        project,
      )
      expect(result).toEqual({ lng: 100, lat: 0, type: "vertex" })
    })

    it("returns external when first vertex is further away", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = {
        actionInstances: {
          draw__polygon: {
            lineDrawer: { shapeLngLats: [[100, 0]] },
          },
        },
      } as any

      const result = mergeExternalSnapWithDrawFirstVertex(
        300,
        0,
        { lng: 200, lat: 0, type: "vertex" },
        project,
      )
      expect(result).toEqual({ lng: 200, lat: 0, type: "vertex" })
    })

    it("returns external when first vertex is outside CORNER_PX", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = {
        actionInstances: {
          draw__polygon: {
            lineDrawer: { shapeLngLats: [[500, 0]] },
          },
        },
      } as any

      const external = makeExternal({ lng: 50 })
      const result = mergeExternalSnapWithDrawFirstVertex(0, 0, external, project)
      expect(result).toEqual(external)
    })

    it("handles draw__line action type", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.geoman = {
        actionInstances: {
          draw__line: {
            lineDrawer: { shapeLngLats: [[99, 0]] },
          },
        },
      } as any

      const result = mergeExternalSnapWithDrawFirstVertex(99, 0, null, project)
      expect(result).toEqual({ lng: 99, lat: 0, type: "vertex" })
    })
  })

  describe("findNearestSnap", () => {
    beforeEach(() => {
      mockGetSnapRings.mockReturnValue([])
      mockGetRoadChains.mockReturnValue([])
      mockGetCityCenterCircles.mockReturnValue([])
      mockGetSnapPoints.mockReturnValue([])
    })

    it("returns null when no snap sources provide data", () => {
      const result = findNearestSnap(0, 0, ["areas"], false)
      expect(result).toBeNull()
    })

    it("snaps to the nearest vertex", () => {
      mockGetSnapRings.mockReturnValue([
        [
          { lat: 10, lng: 10 },
          { lat: 20, lng: 10 },
          { lat: 15, lng: 20 },
        ],
      ])

      const result = findNearestSnap(10, -10, ["areas"], false)
      expect(result).not.toBeNull()
      expect(result!.type).toBe("vertex")
      expect(result!.lng).toBe(10)
      expect(result!.lat).toBe(10)
    })

    it("snaps to the closest vertex when multiple exist", () => {
      mockGetSnapRings.mockReturnValue([
        [
          { lat: 0, lng: 0 },
          { lat: 100, lng: 0 },
          { lat: 0, lng: 100 },
        ],
      ])

      const result = findNearestSnap(2, -2, ["areas"], false)
      expect(result).not.toBeNull()
      expect(result!.lng).toBe(0)
      expect(result!.lat).toBe(0)
    })

    it("snaps to midpoint when includeMidpoint is true", () => {
      mockGetSnapRings.mockReturnValue([
        [
          { lat: 0, lng: 0 },
          { lat: 0, lng: 100 },
          { lat: 100, lng: 100 },
        ],
      ])

      const result = findNearestSnap(50, 0, ["areas"], true)
      expect(result).not.toBeNull()
      expect(result!.type).toBe("midpoint")
    })

    it("snaps to edge when vertex and midpoint are out of range", () => {
      mockGetSnapRings.mockReturnValue([
        [
          { lat: 0, lng: 0 },
          { lat: 0, lng: 100 },
          { lat: 100, lng: 100 },
        ],
      ])

      const result = findNearestSnap(50, -39, ["areas"], false)
      expect(result).not.toBeNull()
      expect(result!.type).toBe("edge")
    })

    it("snaps to circle perimeter", () => {
      mockGetCityCenterCircles.mockReturnValue([
        { lat: 10, lng: 10, radius: 1000000 },
      ])

      const result = findNearestSnap(15, -10, ["cityCenter"], false)
      expect(result).not.toBeNull()
      expect(result!.type).toBe("circle")
    })

    it("vertex beats midpoint when both in range", () => {
      mockGetSnapRings.mockReturnValue([
        [
          { lat: 0, lng: 0 },
          { lat: 0, lng: 100 },
        ],
      ])

      const result = findNearestSnap(0, 0, ["areas"], true)
      expect(result).not.toBeNull()
      expect(result!.type).toBe("vertex")
    })

    it("returns null when nothing is within threshold", () => {
      mockGetSnapRings.mockReturnValue([
        [
          { lat: 0, lng: 0 },
          { lat: 0, lng: 100 },
        ],
      ])

      const result = findNearestSnap(500, -500, ["areas"], true)
      expect(result).toBeNull()
    })

    it("excludes the entry with the given excludeId", () => {
      mockGetSnapRings.mockImplementation(
        (_phaseKeys: string[], excludeId?: string | null) => {
          if (excludeId === "exclude-me") return []
          return [
            [
              { lat: 0, lng: 0 },
              { lat: 10, lng: 0 },
            ],
          ]
        },
      )

      const withExclude = findNearestSnap(0, 0, ["areas"], false, "exclude-me")
      expect(withExclude).toBeNull()

      const withoutExclude = findNearestSnap(0, 0, ["areas"], false)
      expect(withoutExclude).not.toBeNull()
    })

    it("culls vertices outside the viewport bounds", () => {
      mockGetBounds.mockReturnValue({
        getSouth: () => 10,
        getNorth: () => 20,
        getWest: () => 10,
        getEast: () => 20,
      })
      mockGetSnapRings.mockReturnValue([
        [
          { lat: 50, lng: 50 },
          { lat: 15, lng: 15 },
        ],
      ])

      const result = findNearestSnap(15, -15, ["areas"], false)
      expect(result).not.toBeNull()
      expect(result!.lng).toBe(15)
      expect(result!.lat).toBe(15)
    })

    it("snaps from road chains", () => {
      mockGetRoadChains.mockReturnValue([
        [
          { lat: 5, lng: 10 },
          { lat: 5, lng: 20 },
        ],
      ])

      const result = findNearestSnap(10, -5, ["roads"], false)
      expect(result).not.toBeNull()
      expect(result!.lng).toBe(10)
    })

    it("snaps from snap points", () => {
      mockGetSnapPoints.mockReturnValue([
        { lat: 10, lng: 20 },
      ])

      const result = findNearestSnap(20, -10, ["areas"], false)
      expect(result).not.toBeNull()
      expect(result!.type).toBe("vertex")
      expect(result!.lng).toBe(20)
      expect(result!.lat).toBe(10)
    })
  })
})
