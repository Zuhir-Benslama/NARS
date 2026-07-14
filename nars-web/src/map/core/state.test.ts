import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import type maplibregl from "maplibre-gl"

const mockGetSource = vi.fn()
const mockMap = {
  getSource: mockGetSource,
} as unknown as maplibregl.Map

let mod: typeof import("./state")

beforeEach(async () => {
  vi.resetModules()
  vi.clearAllMocks()
  setActivePinia(createPinia())
  mod = await import("./state")
})

afterEach(() => {
  mod.resetMapState()
})

describe("ctx / getCtx", () => {
  it("getCtx throws before initMap", () => {
    expect(() => mod.getCtx()).toThrow("accessed before initMap")
  })

  it("_setCtx + getCtx returns context", () => {
    mod._setCtx({ map: mockMap, geoman: undefined })
    const ctx = mod.getCtx()
    expect(ctx.map).toBe(mockMap)
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

describe("featuresStore (Pinia)", () => {
  const featuresSource = { setData: vi.fn() }

  beforeEach(() => {
    mod._setCtx({ map: mockMap, featuresSource: featuresSource as any })
  })

  it("add appends feature and calls updateSource", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    const f = {
      id: "1",
      geometry: { type: "Point", coordinates: [0, 0] } as any,
      properties: { phaseKey: "areas", label: "A" },
    }
    store.add(f)

    expect(store.getAll()).toHaveLength(1)
    expect(featuresSource.setData).toHaveBeenCalledTimes(1)
  })

  it("batchAdd pushes multiple features", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    const f1 = {
      id: "1",
      geometry: { type: "Point", coordinates: [0, 0] } as any,
      properties: { phaseKey: "areas", label: "A" },
    }
    const f2 = {
      id: "2",
      geometry: { type: "Point", coordinates: [1, 1] } as any,
      properties: { phaseKey: "roads", label: "R" },
    }
    store.batchAdd([f1, f2])

    expect(store.getAll()).toHaveLength(2)
  })

  it("clear empties the store", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    store.add({
      id: "1",
      geometry: { type: "Point", coordinates: [0, 0] } as any,
      properties: { phaseKey: "areas", label: "A" },
    })
    store.clear()

    expect(store.getAll()).toHaveLength(0)
  })

  it("remove filters out by id", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    store.add({
      id: "1",
      geometry: { type: "Point", coordinates: [0, 0] } as any,
      properties: { phaseKey: "areas", label: "A" },
    })
    store.add({
      id: "2",
      geometry: { type: "Point", coordinates: [1, 1] } as any,
      properties: { phaseKey: "roads", label: "R" },
    })
    store.remove("1")

    expect(store.getAll()).toHaveLength(1)
    expect(store.getAll()[0].id).toBe("2")
  })

  it("update patches geometry and properties", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    store.add({
      id: "1",
      geometry: { type: "Point", coordinates: [0, 0] } as any,
      properties: { phaseKey: "areas", label: "A" },
    })
    store.update("1", {
      geometry: { type: "Point", coordinates: [2, 3] } as any,
      properties: { label: "Updated", phaseKey: "areas" },
    })

    const f = store.getAll()[0]
    expect((f.geometry as any).coordinates).toEqual([2, 3])
    expect(f.properties.label).toBe("Updated")
    expect(f.properties.phaseKey).toBe("areas")
  })

  it("update does nothing for missing id", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    const initial = store.getAll().length
    store.update("nonexistent", { properties: { phaseKey: "areas", label: "X" } })
    expect(store.getAll().length).toBe(initial)
  })

  it("updateSource warns when featuresSource is not set", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    mod.resetMapState()
    mod._setCtx({ map: mockMap })
    const store = useFeaturesStore()
    store.updateSource()

    expect(featuresSource.setData).not.toHaveBeenCalled()
  })

  it("updateSource sets feature collection without id field", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    const f1 = {
      id: "1",
      geometry: { type: "Point", coordinates: [0, 0] } as any,
      properties: { label: "A", phaseKey: "areas" },
    }
    const f2 = {
      id: "2",
      geometry: {
        type: "LineString",
        coordinates: [
          [0, 0],
          [1, 1],
        ],
      } as any,
      properties: { label: "R", phaseKey: "roads" },
    }
    store.batchAdd([f1, f2])
    featuresSource.setData.mockClear()
    store.updateSource()

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

  it("sets selection to matching feature geometry", async () => {
    const { useFeaturesStore } = await import("../../stores/featuresStore")
    const store = useFeaturesStore()
    const f = {
      id: "1",
      geometry: { type: "Point", coordinates: [10, 20] } as any,
      properties: { phaseKey: "areas", label: "A", dbId: "1" },
    }
    store.add(f)
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
