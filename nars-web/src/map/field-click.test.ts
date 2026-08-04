import { describe, it, expect, vi, beforeEach } from "vitest"
import { registerFieldWorkerClick, unregisterFieldWorkerClick } from "./field-click"
import type { MapGeoJSONFeature } from "maplibre-gl"

const { mockSelectFeature, mockMapOn, mockQueryRenderedFeatures } = vi.hoisted(() => ({
  mockSelectFeature: vi.fn(),
  mockMapOn: vi.fn(),
  mockQueryRenderedFeatures: vi.fn(),
}))

vi.mock("../stores/fieldStore", () => ({
  useFieldStore: () => ({
    selectFeature: mockSelectFeature,
  }),
}))

vi.mock("../stores/appStore", () => ({
  useAppStore: vi.fn(),
}))

const mockMapOff = vi.fn()

vi.mock("./core/state", () => ({
  getCtx: () => ({
    map: {
      on: mockMapOn,
      off: mockMapOff,
      queryRenderedFeatures: mockQueryRenderedFeatures,
    },
  }),
}))

import { useAppStore } from "../stores/appStore"

function makeMockFeature(overrides: Partial<MapGeoJSONFeature> = {}): MapGeoJSONFeature {
  return {
    type: "Feature",
    id: 1,
    _geometry: { type: "Point", coordinates: [0, 0] },
    properties: { phaseKey: "areas", dbId: "abc" },
    source: "nars-features",
    sourceLayer: "",
    state: {},
    layer: {
      id: "nars-features-layer",
      type: "fill",
      source: "nars-features",
      layout: {},
      paint: {},
      filter: [],
      metadata: {},
      minzoom: 0,
      maxzoom: 22,
    },
    _x: 0,
    _y: 0,
    toJSON: () => ({}),
    ...overrides,
  } as unknown as MapGeoJSONFeature
}

describe("registerFieldWorkerClick", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("does not register click when user role is not field_worker", () => {
    vi.mocked(useAppStore).mockReturnValue({
      user: { role: "commune_user" },
    } as ReturnType<typeof useAppStore>)

    registerFieldWorkerClick()

    expect(mockMapOn).not.toHaveBeenCalled()
  })

  it("registers click handler when user is field_worker", () => {
    vi.mocked(useAppStore).mockReturnValue({
      user: { role: "field_worker" },
    } as ReturnType<typeof useAppStore>)

    registerFieldWorkerClick()

    expect(mockMapOn).toHaveBeenCalledWith("click", expect.any(Function))
  })

  it("does not select when no features at click point", () => {
    let capturedHandler: (...args: unknown[]) => void = () => undefined
    mockMapOn.mockImplementation((_event: string, handler: (...args: unknown[]) => void) => {
      capturedHandler = handler
    })

    vi.mocked(useAppStore).mockReturnValue({
      user: { role: "field_worker" },
    } as ReturnType<typeof useAppStore>)

    registerFieldWorkerClick()

    mockQueryRenderedFeatures.mockReturnValue([])
    capturedHandler({ point: { x: 0, y: 0 } })
    expect(mockSelectFeature).not.toHaveBeenCalled()
  })

  it("selects the topmost nars-features feature", () => {
    let capturedHandler: (...args: unknown[]) => void = () => undefined
    mockMapOn.mockImplementation((_event: string, handler: (...args: unknown[]) => void) => {
      capturedHandler = handler
    })

    vi.mocked(useAppStore).mockReturnValue({
      user: { role: "field_worker" },
    } as ReturnType<typeof useAppStore>)

    registerFieldWorkerClick()

    mockQueryRenderedFeatures.mockReturnValue([
      makeMockFeature({
        properties: { phaseKey: "roads", dbId: "road-1", label: "Main Road" },
      }),
    ])
    capturedHandler({ point: { x: 10, y: 20 } })
    expect(mockSelectFeature).toHaveBeenCalledWith({
      id: "road-1",
      label: "Main Road",
      type: "road",
    })
  })

  it("ignores features with unmapped phaseKey", () => {
    let capturedHandler: (...args: unknown[]) => void = () => undefined
    mockMapOn.mockImplementation((_event: string, handler: (...args: unknown[]) => void) => {
      capturedHandler = handler
    })

    vi.mocked(useAppStore).mockReturnValue({
      user: { role: "field_worker" },
    } as ReturnType<typeof useAppStore>)

    registerFieldWorkerClick()

    mockQueryRenderedFeatures.mockReturnValue([
      makeMockFeature({
        properties: { phaseKey: "areas", dbId: "area-1" },
      }),
    ])
    capturedHandler({ point: { x: 0, y: 0 } })
    expect(mockSelectFeature).not.toHaveBeenCalled()
  })
})

describe("unregisterFieldWorkerClick", () => {
  it("unbinds the click handler", () => {
    unregisterFieldWorkerClick()
    expect(mockMapOff).toHaveBeenCalledWith("click", expect.any(Function))
  })
})
