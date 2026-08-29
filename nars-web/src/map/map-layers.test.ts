import { describe, it, expect, vi, beforeEach, type Mock } from "vitest"
import type { Map as MapLibreMap } from "maplibre-gl"
import { _setCtx, resetMapState } from "./core/state"

const mockAddBoundaryClickEvents = vi.hoisted(() => vi.fn())

vi.mock("./map-boundary", () => ({
  addBoundaryClickEvents: mockAddBoundaryClickEvents,
  resetBoundaryEvents: vi.fn(),
  removeBoundaryClickEvents: vi.fn(),
}))

let mod: typeof import("./map-layers")

interface MapMock {
  sources: Record<string, { type: string; data?: unknown }>
  addSource: Mock<(id: string, source: { type: string; data?: unknown }) => void>
  getSource: Mock<(id: string) => unknown>
  addLayer: Mock<(layer: unknown) => void>
  getStyle: Mock<() => { layers: unknown[] }>
}

function makeMap(): MapMock {
  const sources: Record<string, { type: string; data?: unknown }> = {}
  const map: MapMock = {
    sources,
    addSource: vi.fn((id: string, source: { type: string; data?: unknown }) => {
      sources[id] = source
    }),
    getSource: vi.fn((id: string) => sources[id] ?? null),
    addLayer: vi.fn(),
    getStyle: vi.fn(() => ({ layers: [] })),
  }
  return map
}

function asMap(map: MapMock): MapLibreMap {
  return map as unknown as MapLibreMap
}

beforeEach(async () => {
  vi.clearAllMocks()
  resetMapState()
  mod = await import("./map-layers")
})

describe("initSources", () => {
  it("adds missing sources and wires context sources", () => {
    const map = makeMap()
    _setCtx({ map: asMap(map) })

    mod.initSources()

    expect(map.addSource).toHaveBeenCalledTimes(5)
    for (const name of ["boundaries", "scattered", "features", "selection", "endpoints"]) {
      expect(map.addSource).toHaveBeenCalledWith(name, {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      })
    }
    expect(mockAddBoundaryClickEvents).toHaveBeenCalled()
  })

  it("does not re-add existing sources", () => {
    const map = makeMap()
    map.addSource("features", {
      type: "geojson",
      data: { type: "FeatureCollection", features: [] },
    })
    map.addSource.mockClear()
    _setCtx({ map: asMap(map) })

    mod.initSources()

    expect(map.addSource).not.toHaveBeenCalledWith("features", expect.any(Object))
  })

  it("throws when a source is missing after setup", () => {
    const map = makeMap()
    map.getSource = vi.fn(() => null)
    _setCtx({ map: asMap(map) })

    expect(() => mod.initSources()).toThrow(/Source "boundaries" not found/)
  })
})

describe("addFeatureLayers / addEndpointLayers", () => {
  it("adds all feature layers and boundary events", () => {
    const map = makeMap()
    mod.addFeatureLayers(asMap(map))
    expect(map.addLayer).toHaveBeenCalled()
    expect(mockAddBoundaryClickEvents).toHaveBeenCalledWith(map)
  })

  it("adds all endpoint layers", () => {
    const map = makeMap()
    mod.addEndpointLayers(asMap(map))
    const calls = map.addLayer.mock.calls
    expect(calls.some((c) => (c[0] as { id: string })?.id === "nars-endpoint-start")).toBe(true)
    expect(calls.some((c) => (c[0] as { id: string })?.id === "nars-endpoint-end-label")).toBe(true)
  })

  it("adds the expected feature-layer ids", () => {
    const map = makeMap()
    mod.addFeatureLayers(asMap(map))
    const calls = map.addLayer.mock.calls
    expect(calls.some((c) => (c[0] as { id: string })?.id === "nars-polygon-fill")).toBe(true)
    expect(calls.some((c) => (c[0] as { id: string })?.id === "nars-point-label")).toBe(true)
  })
})
