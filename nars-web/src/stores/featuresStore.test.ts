import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { MaplibreFeature } from "../map/core/state"

const mockSetData = vi.fn()
const mockDebugWarn = vi.fn()
const mockDebugLog = vi.fn()

vi.mock("../map/core/state", () => ({
  getCtx: () => ({ featuresSource: { setData: mockSetData } }),
}))

vi.mock("../utils/debug", () => ({
  debugWarn: mockDebugWarn,
  debugLog: mockDebugLog,
}))

let useFeaturesStore: any

function feature(id: string): MaplibreFeature {
  return {
    id,
    type: "Feature",
    geometry: { type: "Point", coordinates: [0, 0] },
    properties: { name: id },
  } as unknown as MaplibreFeature
}

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  useFeaturesStore = (await import("./featuresStore")).useFeaturesStore
})

describe("featuresStore.batchUpdate", () => {
  it("applies geometry + property patches and calls setData once", () => {
    const store = useFeaturesStore()
    store.batchAdd([feature("a"), feature("b"), feature("c")])
    mockSetData.mockClear()

    store.batchUpdate([
      { id: "a", geometry: { type: "Point", coordinates: [1, 1] }, properties: { phaseKey: "x" } },
      { id: "b", properties: { label: "2" } },
    ])

    expect(store.features[0].geometry).toEqual({ type: "Point", coordinates: [1, 1] })
    expect(store.features[0].properties).toEqual({ name: "a", phaseKey: "x" })
    expect(store.features[1].properties).toEqual({ name: "b", label: "2" })
    expect(store.features[2].geometry).toEqual({ type: "Point", coordinates: [0, 0] })
    expect(mockSetData).toHaveBeenCalledTimes(1)
  })

  it("does not merge properties when only geometry is patched", () => {
    const store = useFeaturesStore()
    store.batchAdd([feature("a")])
    mockSetData.mockClear()

    store.batchUpdate([{ id: "a", geometry: { type: "Point", coordinates: [5, 5] } }])

    expect(store.features[0].properties).toEqual({ name: "a" })
    expect(mockSetData).toHaveBeenCalledTimes(1)
  })

  it("warns and skips unknown ids without throwing", () => {
    const store = useFeaturesStore()
    store.batchAdd([feature("a")])
    mockSetData.mockClear()

    store.batchUpdate([{ id: "ghost", properties: { label: "x" } }])

    expect(mockDebugWarn).toHaveBeenCalledWith(
      "featuresStore.batchUpdate: feature not found",
      "ghost",
    )
    expect(store.features).toHaveLength(1)
    expect(mockSetData).toHaveBeenCalledTimes(1)
  })

  it("handles an empty patch list by calling setData once", () => {
    const store = useFeaturesStore()
    store.batchAdd([feature("a")])
    mockSetData.mockClear()

    store.batchUpdate([])

    expect(mockSetData).toHaveBeenCalledTimes(1)
  })
})
