import { describe, it, expect, vi, beforeEach } from "vitest"

const { mockSetStyle, mockOnce, mockCreateGeoman, mockInitSources, mockUpdateSource, mockCtx } =
  vi.hoisted(() => ({
    mockSetStyle: vi.fn(),
    mockOnce: vi.fn(),
    mockCreateGeoman: vi.fn(),
    mockInitSources: vi.fn(),
    mockUpdateSource: vi.fn(),
    mockCtx: {} as {
      map?: {
        setStyle: unknown
        once: unknown
        doubleClickZoom: { disable: () => void }
        on: unknown
        off: unknown
      }
      satelliteStyle?: unknown
      streetStyle?: unknown
      darkStyle?: unknown
      lightStyle?: unknown
      geoman?: { destroyed: boolean; destroy: () => Promise<void> }
      [key: string]: unknown
    },
  }))

const styleLoadHandlers: (() => void)[] = []
mockOnce.mockImplementation((event: string, cb: () => void) => {
  if (event === "style.load") styleLoadHandlers.push(cb)
})

vi.mock("maplibre-gl", () => ({
  default: {
    Map: class {
      setStyle = mockSetStyle
      once = mockOnce
      doubleClickZoom = { disable: vi.fn() }
      on = vi.fn()
      off = vi.fn()
    },
  },
}))

vi.mock("@geoman-io/maplibre-geoman-free", () => ({
  createGeomanInstance: mockCreateGeoman,
}))

vi.mock("./core/state", () => ({
  getCtx: () => mockCtx,
  _setCtx: (ctx: unknown) => {
    Object.assign(mockCtx, ctx)
  },
}))

vi.mock("./map-layers", () => ({ initSources: mockInitSources }))
vi.mock("./edit/edit-mode", () => ({
  suppressGeomanFill: vi.fn(),
  ensureGeomanDrawEdgesVisible: vi.fn(),
}))
vi.mock("./roads/road-directions", () => ({ updateEndpointMarkers: vi.fn() }))
vi.mock("./rendering/labels", () => ({ refreshLayerVisibility: vi.fn() }))
vi.mock("../stores/featuresStore", () => ({
  useFeaturesStore: () => ({ updateSource: mockUpdateSource }),
}))

import { initMap, setBaseLayer, resetMapInit } from "./map-init"

describe("switchBaseLayer", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    styleLoadHandlers.length = 0
    Object.assign(mockCtx, {
      map: {
        setStyle: mockSetStyle,
        once: mockOnce,
        doubleClickZoom: { disable: vi.fn() },
        on: vi.fn(),
        off: vi.fn(),
      },
      satelliteStyle: { version: 8, sources: {}, layers: [] },
      streetStyle: { version: 8, sources: {}, layers: [] },
      darkStyle: { version: 8, sources: {}, layers: [] },
      lightStyle: { version: 8, sources: {}, layers: [] },
      geoman: { destroyed: false, destroy: vi.fn().mockResolvedValue(undefined) },
    })
    mockCreateGeoman.mockResolvedValue({
      destroyed: false,
      destroy: vi.fn().mockResolvedValue(undefined),
    })
    resetMapInit()
  })

  it("ignores a concurrent style switch while one is in flight", async () => {
    // Wire the internal setBaseLayer implementation via initMap.
    const initPromise = initMap()
    const mapLoadHandler = mockOnce.mock.calls.find((c) => c[0] === "load")?.[1] as
      (() => void) | undefined
    expect(mapLoadHandler).toBeDefined()
    mapLoadHandler!()
    await initPromise

    mockOnce.mockClear()
    mockSetStyle.mockClear()
    mockInitSources.mockClear()
    mockUpdateSource.mockClear()
    mockCreateGeoman.mockClear()

    const first = setBaseLayer("street")
    const second = setBaseLayer("dark")

    expect(mockSetStyle).toHaveBeenCalledTimes(1)

    // Let the first switch finish.
    const styleHandler = styleLoadHandlers[0]
    expect(styleHandler).toBeDefined()
    styleHandler!()
    await first
    await second

    // Concurrent request was dropped — exactly one full switch cycle.
    expect(mockSetStyle).toHaveBeenCalledTimes(1)
    expect(mockInitSources).toHaveBeenCalledTimes(1)
    expect(mockUpdateSource).toHaveBeenCalledTimes(1)
    expect(mockCreateGeoman).toHaveBeenCalledTimes(1) // post-switch rebuild only
  })

  it("allows a subsequent switch after the previous one completed", async () => {
    const initPromise = initMap()
    const mapLoadHandler = mockOnce.mock.calls.find((c) => c[0] === "load")?.[1] as
      (() => void) | undefined
    mapLoadHandler!()
    await initPromise

    mockOnce.mockClear()
    mockSetStyle.mockClear()
    mockInitSources.mockClear()
    mockUpdateSource.mockClear()

    const first = setBaseLayer("street")
    styleLoadHandlers[0]!()
    await first
    mockInitSources.mockClear()
    mockSetStyle.mockClear()

    const second = setBaseLayer("dark")
    styleLoadHandlers[1]!()
    await second

    expect(mockSetStyle).toHaveBeenCalledTimes(1)
    expect(mockInitSources).toHaveBeenCalledTimes(1)
  })
})
