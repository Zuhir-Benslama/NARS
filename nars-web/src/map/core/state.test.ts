import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import type maplibregl from "maplibre-gl"

const mockSetData = vi.fn()
const mockGetSource = vi.fn()
const mockMap = {
  getSource: mockGetSource,
} as unknown as maplibregl.Map

let mod: typeof import("./state")

beforeEach(async () => {
  vi.resetModules()
  vi.clearAllMocks()
  mod = await import("./state")
})

afterEach(() => {
  mod.resetMapState()
})

describe("ctx / getCtx", () => {
  it("getCtx throws before initMap", () => {
    expect(() => mod.getCtx()).toThrow("accessed before initMap")
  })

  it("ctx proxy throws on property access before init", () => {
    expect(() => mod.ctx.map).toThrow("ctx.map accessed before initMap")
    expect(() => mod.ctx.geoman).toThrow("ctx.geoman accessed before initMap")
  })

  it("_setCtx + getCtx returns context", () => {
    mod._setCtx({ map: mockMap, geoman: undefined })
    const ctx = mod.getCtx()
    expect(ctx.map).toBe(mockMap)
  })

  it("ctx proxy works after _setCtx", () => {
    mod._setCtx({ map: mockMap })
    expect(mod.ctx.map).toBe(mockMap)
  })

  it("resetMapState resets ctx to pre-init state", () => {
    mod._setCtx({ map: mockMap })
    mod.resetMapState()
    expect(() => mod.getCtx()).toThrow("accessed before initMap")
  })

  it("_setCtx can update context", () => {
    mod._setCtx({ map: mockMap })
    mod._setCtx({ map: mockMap, geoman: undefined })
    expect(mod.getCtx().geoman).toBeUndefined()
  })
})

describe("featuresStore", () => {
  const featuresSource = { setData: vi.fn() }

  beforeEach(() => {
    mod._setCtx({ map: mockMap, featuresSource: featuresSource as any })
  })

  it("add appends feature and calls updateSource", () => {
    const f = { id: "1", geometry: { type: "Point", coordinates: [0, 0] } as any, properties: { phaseKey: "areas", label: "A" } }
    mod.featuresStore.add(f)

    expect(mod.featuresStore.getAll()).toHaveLength(1)
    expect(featuresSource.setData).toHaveBeenCalledTimes(1)
  })

  it("batchAdd pushes multiple features", () => {
    const f1 = { id: "1", geometry: { type: "Point", coordinates: [0, 0] } as any, properties: { phaseKey: "areas", label: "A" } }
    const f2 = { id: "2", geometry: { type: "Point", coordinates: [1, 1] } as any, properties: { phaseKey: "roads", label: "R" } }
    mod.featuresStore.batchAdd([f1, f2])

    expect(mod.featuresStore.getAll()).toHaveLength(2)
  })

  it("clear empties the store", () => {
    mod.featuresStore.add({ id: "1", geometry: { type: "Point", coordinates: [0, 0] } as any, properties: { phaseKey: "areas", label: "A" } })
    mod.featuresStore.clear()

    expect(mod.featuresStore.getAll()).toHaveLength(0)
  })

  it("remove filters out by id", () => {
    mod.featuresStore.add({ id: "1", geometry: { type: "Point", coordinates: [0, 0] } as any, properties: { phaseKey: "areas", label: "A" } })
    mod.featuresStore.add({ id: "2", geometry: { type: "Point", coordinates: [1, 1] } as any, properties: { phaseKey: "roads", label: "R" } })
    mod.featuresStore.remove("1")

    expect(mod.featuresStore.getAll()).toHaveLength(1)
    expect(mod.featuresStore.getAll()[0].id).toBe("2")
  })

  it("update patches geometry and properties", () => {
    mod.featuresStore.add({ id: "1", geometry: { type: "Point", coordinates: [0, 0] } as any, properties: { phaseKey: "areas", label: "A" } })
    mod.featuresStore.update("1", {
      geometry: { type: "Point", coordinates: [2, 3] } as any,
      properties: { label: "Updated" },
    })

    const f = mod.featuresStore.getAll()[0]
    expect((f.geometry as any).coordinates).toEqual([2, 3])
    expect(f.properties.label).toBe("Updated")
    expect(f.properties.phaseKey).toBe("areas")
  })

  it("update does nothing for missing id", () => {
    const initial = mod.featuresStore.getAll().length
    mod.featuresStore.update("nonexistent", { properties: { phaseKey: "areas", label: "X" } })
    expect(mod.featuresStore.getAll().length).toBe(initial)
  })

  it("updateSource warns when featuresSource is not set", () => {
    mod.resetMapState()
    mod._setCtx({ map: mockMap })
    mod.featuresStore.updateSource()

    expect(featuresSource.setData).not.toHaveBeenCalled()
  })

  it("updateSource sets feature collection without id field", () => {
    const f1 = { id: "1", geometry: { type: "Point", coordinates: [0, 0] } as any, properties: { label: "A", phaseKey: "areas" } }
    const f2 = { id: "2", geometry: { type: "LineString", coordinates: [[0, 0], [1, 1]] } as any, properties: { label: "R", phaseKey: "roads" } }
    mod.featuresStore.batchAdd([f1, f2])
    featuresSource.setData.mockClear()
    mod.featuresStore.updateSource()

    expect(featuresSource.setData).toHaveBeenCalledTimes(1)
    const data = featuresSource.setData.mock.calls[0][0] as GeoJSON.FeatureCollection
    expect(data.type).toBe("FeatureCollection")
    expect(data.features).toHaveLength(2)
    for (const f of data.features) {
      expect((f as any).id).toBeUndefined()
    }
  })
})

describe("updateSelectionHighlight", () => {
  let featuresSource: { setData: any }
  let selectionSource: { setData: any }

  beforeEach(() => {
    featuresSource = { setData: vi.fn() }
    selectionSource = { setData: vi.fn() }
    mockGetSource.mockReturnValue(selectionSource)
    mod._setCtx({ map: mockMap, featuresSource: featuresSource as any })
  })

  it("clears selection when dbId is null", () => {
    mod.updateSelectionHighlight(null)
    expect(selectionSource.setData).toHaveBeenCalledWith({
      type: "FeatureCollection",
      features: [],
    })
  })

  it("clears selection when feature not found", () => {
    mod.updateSelectionHighlight("nonexistent")
    expect(selectionSource.setData).toHaveBeenCalledWith({
      type: "FeatureCollection",
      features: [],
    })
  })

  it("sets selection to matching feature geometry", () => {
    const f = { id: "1", geometry: { type: "Point", coordinates: [10, 20] } as any, properties: { phaseKey: "areas", label: "A", dbId: "1" } }
    mod.featuresStore.add(f)
    mod.updateSelectionHighlight("1")

    expect(selectionSource.setData).toHaveBeenCalled()
    const data = selectionSource.setData.mock.calls[0][0] as GeoJSON.FeatureCollection
    expect(data.features).toHaveLength(1)
    expect(data.features[0].geometry).toEqual(f.geometry)
  })

  it("does nothing when selection source does not exist", () => {
    mockGetSource.mockReturnValue(undefined)
    mod.updateSelectionHighlight("1")

    expect(selectionSource.setData).not.toHaveBeenCalled()
  })
})
