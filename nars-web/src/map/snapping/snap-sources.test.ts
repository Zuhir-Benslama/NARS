import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

let _setCtx: (ctx: any) => void
let mod: typeof import("./snap-sources")
let useLayerStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())

  const stateMod = await import("../core/state")
  _setCtx = stateMod._setCtx
  _setCtx({} as any)

  mod = await import("./snap-sources")
  const ls = await import("../../stores/layerStore")
  useLayerStore = ls.useLayerStore
})

describe("setSnapSourceExclude", () => {
  it("sets snapExclude on snapStore", async () => {
    mod.setSnapSourceExclude("feat_1")
    const { useSnapStore } = await import("../../stores/snapStore")
    expect(useSnapStore().snapExclude).toBe("feat_1")
  })
})

describe("getSnapRings", () => {
  function addPoly(phaseKey: string, id: string, coords: { lat: number; lng: number }[]) {
    useLayerStore().addFeature(phaseKey, {
      id,
      dbId: id,
      type: "polygon",
      data: { type: phaseKey, label: "P", coordinates: coords },
    } as any)
  }

  it("returns empty array when no polygon entries", () => {
    const result = mod.getSnapRings(["areas"])
    expect(result).toEqual([])
  })

  it("collects polygon rings from matching phase keys", () => {
    addPoly("areas", "a1", [
      { lat: 0, lng: 0 },
      { lat: 1, lng: 0 },
      { lat: 0, lng: 1 },
    ])

    const result = mod.getSnapRings(["areas"])

    expect(result).toHaveLength(1)
    expect(result[0]).toHaveLength(3)
  })

  it("excludes entry by id", () => {
    addPoly("areas", "keep", [
      { lat: 0, lng: 0 },
      { lat: 1, lng: 0 },
      { lat: 0, lng: 1 },
    ])
    addPoly("areas", "skip", [
      { lat: 2, lng: 2 },
      { lat: 3, lng: 2 },
      { lat: 2, lng: 3 },
    ])

    const result = mod.getSnapRings(["areas"], "skip")

    expect(result).toHaveLength(1)
    expect(result[0][0]).toEqual({ lat: 0, lng: 0 })
  })

  it("skips non-polygon entries", () => {
    useLayerStore().addFeature("areas", {
      id: "line1",
      dbId: "line1",
      type: "line",
      data: {
        type: "areas",
        label: "L",
        coordinates: [
          { lat: 0, lng: 0 },
          { lat: 1, lng: 1 },
        ],
      },
    } as any)

    const result = mod.getSnapRings(["areas"])

    expect(result).toEqual([])
  })

  it("skips polygon with fewer than 3 coordinates", () => {
    addPoly("areas", "short", [
      { lat: 0, lng: 0 },
      { lat: 1, lng: 0 },
    ])

    const result = mod.getSnapRings(["areas"])

    expect(result).toEqual([])
  })

  it("includes boundary rings when ctx.boundariesGeoJson is set", async () => {
    _setCtx({
      boundariesGeoJson: {
        type: "FeatureCollection",
        features: [
          {
            type: "Feature",
            geometry: {
              type: "Polygon",
              coordinates: [
                [
                  [127.0, 36.0],
                  [127.1, 36.0],
                  [127.05, 36.1],
                  [127.0, 36.0],
                ],
              ],
            },
            properties: {},
          },
        ],
      },
    } as any)

    const result = mod.getSnapRings([])

    expect(result).toHaveLength(1)
    expect(result[0][0]).toEqual({ lat: 36.0, lng: 127.0 })
  })
})

describe("getRoadChains", () => {
  it("returns empty when phaseKeys does not include roads", () => {
    expect(mod.getRoadChains(["areas"])).toEqual([])
  })

  it("collects line entries from roads layer", () => {
    useLayerStore().addFeature("roads", {
      id: "r1",
      dbId: "r1",
      type: "line",
      data: {
        type: "roads",
        label: "R",
        coordinates: [
          { lat: 0, lng: 0 },
          { lat: 1, lng: 1 },
        ],
      },
    } as any)

    const result = mod.getRoadChains(["roads"])

    expect(result).toHaveLength(1)
    expect(result[0]).toHaveLength(2)
  })

  it("excludes entry by id", () => {
    useLayerStore().addFeature("roads", {
      id: "exclude",
      dbId: "exclude",
      type: "line",
      data: {
        type: "roads",
        label: "X",
        coordinates: [
          { lat: 0, lng: 0 },
          { lat: 1, lng: 1 },
        ],
      },
    } as any)

    expect(mod.getRoadChains(["roads"], "exclude")).toEqual([])
  })

  it("skips non-line entries", () => {
    useLayerStore().addFeature("roads", {
      id: "r2",
      dbId: "r2",
      type: "polygon",
      data: {
        type: "roads",
        label: "P",
        coordinates: [
          { lat: 0, lng: 0 },
          { lat: 1, lng: 0 },
          { lat: 0, lng: 1 },
        ],
      },
    } as any)

    expect(mod.getRoadChains(["roads"])).toEqual([])
  })
})

describe("getCityCenterCircles", () => {
  it("returns empty when phaseKeys has no cityCenter", () => {
    expect(mod.getCityCenterCircles(["areas"])).toEqual([])
  })

  it("collects entries with valid lat/lng/radius", () => {
    useLayerStore().addFeature("cityCenter", {
      id: "cc1",
      dbId: "cc1",
      type: "circle",
      data: { type: "cityCenter", label: "CC", lat: 36.0, lng: 127.0, radius: 500 },
    } as any)

    const result = mod.getCityCenterCircles(["cityCenter"])

    expect(result).toEqual([{ lat: 36.0, lng: 127.0, radius: 500 }])
  })

  it("skips entries with missing or zero radius", () => {
    useLayerStore().addFeature("cityCenter", {
      id: "cc2",
      dbId: "cc2",
      type: "circle",
      data: { type: "cityCenter", label: "CC", lat: 36.0, lng: 127.0, radius: 0 },
    } as any)

    expect(mod.getCityCenterCircles(["cityCenter"])).toEqual([])
  })
})

describe("getSnapPoints", () => {
  it("collects point features with lat/lng", () => {
    useLayerStore().addFeature("cityCenter", {
      id: "p1",
      dbId: "p1",
      type: "point",
      data: { type: "cityCenter", label: "P", lat: 36.5, lng: 127.5 },
    } as any)

    const result = mod.getSnapPoints(["cityCenter"])

    expect(result).toEqual([{ lat: 36.5, lng: 127.5 }])
  })

  it("skips entries without lat/lng", () => {
    useLayerStore().addFeature("cityCenter", {
      id: "p2",
      dbId: "p2",
      type: "point",
      data: { type: "cityCenter", label: "P" },
    } as any)

    expect(mod.getSnapPoints(["cityCenter"])).toEqual([])
  })
})
