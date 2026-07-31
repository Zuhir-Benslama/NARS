import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

const {
  mockUnpatchGeomanMarker,
  mockRepatchMarker,
  mockIsSnappingEnabled,
  mockSetSnappingEnabled,
  mockFindNearestSnap,
  mockSetSnapSourceExclude,
  mockCtx,
  resetMockDom,
} = vi.hoisted(() => {
  const mockCanvas = document.createElement("canvas")
  let mockContainer = document.createElement("div")
  return {
    mockUnpatchGeomanMarker: vi.fn(),
    mockRepatchMarker: vi.fn(),
    mockIsSnappingEnabled: vi.fn(() => false),
    mockSetSnappingEnabled: vi.fn(),
    mockFindNearestSnap: vi.fn(),
    mockSetSnapSourceExclude: vi.fn(),
    mockCtx: {
      map: {
        getContainer: () => mockContainer,
        getCanvas: () => mockCanvas,
        project: vi.fn(() => ({ x: 100, y: 200 })),
      },
    } as any,
    resetMockDom: () => {
      // Fresh container per test so snap listeners from previous tests
      // (which reference stale module instances) do not accumulate.
      mockContainer = document.createElement("div")
      mockCtx.map.getContainer = () => mockContainer
      mockCanvas.style.cursor = ""
    },
  }
})

vi.mock("../draw/draw-complete", () => ({
  unpatchGeomanMarker: mockUnpatchGeomanMarker,
  repatchMarker: mockRepatchMarker,
  isSnappingEnabled: mockIsSnappingEnabled,
  setSnappingEnabled: mockSetSnappingEnabled,
}))
vi.mock("./snap-search", () => ({
  findNearestSnap: mockFindNearestSnap,
  mergeExternalSnapWithDrawFirstVertex: vi.fn(),
}))
vi.mock("./snap-sources", () => ({
  setSnapSourceExclude: mockSetSnapSourceExclude,
  getSnapRings: vi.fn(),
  getRoadChains: vi.fn(),
  getCityCenterCircles: vi.fn(),
  getSnapPoints: vi.fn(),
}))

let _setCtx: (ctx: any) => void
let mod: typeof import("./snapping")
let useSnapStore: any
let useLayerStore: any
let useAppStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())
  resetMockDom()

  const stateMod = await import("../core/state")
  _setCtx = stateMod._setCtx
  _setCtx(mockCtx)

  mod = await import("./snapping")

  const ss = await import("../../stores/snapStore")
  useSnapStore = ss.useSnapStore
  const ls = await import("../../stores/layerStore")
  useLayerStore = ls.useLayerStore
  const as = await import("../../stores/appStore")
  useAppStore = as.useAppStore
})

describe("resetSnapState", () => {
  it("resets snap store and clears marker/cursor", () => {
    mod.resetSnapState()
    expect(useSnapStore().snapActive).toBe(false)
  })
})

describe("isSnapFrozen / getFrozenSnapPos", () => {
  it("returns snapFrozen state", () => {
    expect(mod.isSnapFrozen()).toBe(false)
    useSnapStore().snapFrozen = true
    expect(mod.isSnapFrozen()).toBe(true)
  })

  it("returns frozen snap position", () => {
    expect(mod.getFrozenSnapPos()).toBeNull()
    useSnapStore().snapFrozen = true
    useSnapStore().snapLatLng = { lat: 36.0, lng: 127.0 }
    expect(mod.getFrozenSnapPos()).toEqual({ lat: 36.0, lng: 127.0 })
  })
})

describe("setEditModeActive / setEditDragActive", () => {
  it("sets edit mode active", () => {
    mod.setEditModeActive(true)
    expect(useSnapStore().editModeActive).toBe(true)
  })

  it("sets edit drag active", () => {
    mod.setEditDragActive(true)
    expect(useSnapStore().editDragActive).toBe(true)
  })
})

describe("setSnapExclude", () => {
  it("delegates to setSnapSourceExclude", () => {
    mod.setSnapExclude("feat_1")
    expect(mockSetSnapSourceExclude).toHaveBeenCalledWith("feat_1")
  })
})

describe("isSnappingActive", () => {
  it("returns snapActive from store", () => {
    expect(mod.isSnappingActive()).toBe(false)
    useSnapStore().snapActive = true
    expect(mod.isSnappingActive()).toBe(true)
  })
})

describe("getActiveSnapPhases", () => {
  it("returns all non-empty phases in edit mode", () => {
    useSnapStore().editModeActive = true
    useLayerStore().addFeature("areas", {
      id: "f1",
      dbId: "1",
      data: { type: "areas", label: "A" } as any,
      type: "geojson",
    })
    useLayerStore().addFeature("roads", {
      id: "f2",
      dbId: "2",
      data: { type: "roads", label: "R" } as any,
      type: "geojson",
    })

    const result = mod.getActiveSnapPhases()

    expect(result).toContain("areas")
    expect(result).toContain("roads")
  })

  it("returns empty array for empty layers in edit mode", () => {
    useSnapStore().editModeActive = true

    const result = mod.getActiveSnapPhases()

    expect(result).toEqual([])
  })
})

describe("enableCrosshair / disableCrosshair", () => {
  it("sets crosshair cursor via canvas", () => {
    mod.enableCrosshair()
    expect(useSnapStore().crosshairActive).toBe(true)
    expect(mockCtx.map.getCanvas().style.cursor).toBe("crosshair")
  })

  it("does not set cursor if already active", () => {
    useSnapStore().crosshairActive = true
    mod.enableCrosshair()
    expect(mockCtx.map.getCanvas().style.cursor).toBe("")
  })

  it("clears cursor on disable", () => {
    mod.enableCrosshair()
    mod.disableCrosshair()
    expect(useSnapStore().crosshairActive).toBe(false)
    expect(mockCtx.map.getCanvas().style.cursor).toBe("")
  })

  it("does nothing if not active", () => {
    mod.disableCrosshair()
    expect(useSnapStore().crosshairActive).toBe(false)
  })
})

describe("toggleSnapping", () => {
  it("enables snapping when disabled", () => {
    mockIsSnappingEnabled.mockReturnValue(false)

    const result = mod.toggleSnapping()

    expect(result).toBe(true)
    expect(mockSetSnappingEnabled).toHaveBeenCalledWith(true)
    expect(mockRepatchMarker).toHaveBeenCalled()
  })

  it("disables snapping when enabled", () => {
    mockIsSnappingEnabled.mockReturnValue(true)
    mockUnpatchGeomanMarker.mockReturnValue(undefined)

    const result = mod.toggleSnapping()

    expect(result).toBe(false)
    expect(mockSetSnappingEnabled).toHaveBeenCalledWith(false)
    expect(mockUnpatchGeomanMarker).toHaveBeenCalled()
  })
})

describe("snapPointForEdit", () => {
  it("returns null when no snap found", () => {
    mockFindNearestSnap.mockReturnValue(null)
    const result = mod.snapPointForEdit(100, 200, null)
    expect(result).toBeNull()
  })

  it("returns {lat, lng} when snap found", () => {
    mockFindNearestSnap.mockReturnValue({ lat: 36.5, lng: 127.5, type: "vertex", distance: 5 })
    const result = mod.snapPointForEdit(100, 200, "feat_1")
    expect(result).toEqual({ lat: 36.5, lng: 127.5 })
  })
})

describe("installSnapInterceptors", () => {
  it("registers click and mousedown handlers", () => {
    const onSpy = vi.fn()
    mockCtx.map.on = onSpy

    mod.installSnapInterceptors()

    expect(onSpy).toHaveBeenCalledWith("click", expect.any(Function))
    expect(onSpy).toHaveBeenCalledWith("mousedown", expect.any(Function))
  })

  it("injects snap lngLat into event when snap is active", () => {
    const handlers: Record<string, Function> = {}
    mockCtx.map.on = (ev: string, fn: Function) => {
      handlers[ev] = fn
    }
    mod.installSnapInterceptors()

    useSnapStore().snapActive = true
    useSnapStore().snapLatLng = { lat: 36.0, lng: 127.5 }

    const e: Record<string, any> = {}
    handlers.click(e)

    expect(e.lngLat).toBeDefined()
    expect(e.lngLat.lat).toBe(36.0)
    expect(e.lngLat.lng).toBe(127.5)
  })
})

describe("getActiveSnapPhases (draw mode)", () => {
  it("returns completed snap targets for the current phase", () => {
    useAppStore().currentPhase = 3

    const result = mod.getActiveSnapPhases()

    expect(result).toContain("areas")
    expect(result).toContain("cityCenter")
    expect(result).toContain("roads")
  })

  it("returns empty array for phases without snap targets", () => {
    useAppStore().currentPhase = 4

    expect(mod.getActiveSnapPhases()).toEqual([])
  })

  it("returns empty array for out-of-range phase", () => {
    useAppStore().currentPhase = 99

    expect(mod.getActiveSnapPhases()).toEqual([])
  })
})

describe("enableSnapping / disableSnapping", () => {
  it("enableSnapping activates snapping and registers listeners", () => {
    mockIsSnappingEnabled.mockReturnValue(false)

    mod.enableSnapping()

    expect(mockSetSnappingEnabled).toHaveBeenCalledWith(true)
    expect(useSnapStore().snapActive).toBe(true)
    expect(mockSetSnapSourceExclude).toHaveBeenCalledWith(null)
    expect(mockRepatchMarker).toHaveBeenCalled()
  })

  it("enableSnapping is a no-op when already enabled", () => {
    mockIsSnappingEnabled.mockReturnValue(true)

    mod.enableSnapping()

    expect(mockSetSnappingEnabled).not.toHaveBeenCalled()
  })

  it("disableSnapping deactivates snapping and unregisters listeners", () => {
    mockIsSnappingEnabled.mockReturnValue(true)
    mockUnpatchGeomanMarker.mockReturnValue(undefined)
    const store = useSnapStore()
    store.snapRafId = 42

    mod.disableSnapping()

    expect(mockSetSnappingEnabled).toHaveBeenCalledWith(false)
    expect(store.snapActive).toBe(false)
    expect(store.snapFrozen).toBe(false)
    expect(mockUnpatchGeomanMarker).toHaveBeenCalled()
  })

  it("disableSnapping is a no-op when not enabled", () => {
    mockIsSnappingEnabled.mockReturnValue(false)

    mod.disableSnapping()

    expect(mockSetSnappingEnabled).not.toHaveBeenCalled()
  })

  it("resetSnapping disables then re-enables", () => {
    mockIsSnappingEnabled.mockReturnValueOnce(true).mockReturnValueOnce(false)

    mod.resetSnapping()

    expect(mockSetSnappingEnabled).toHaveBeenCalledWith(false)
    expect(mockSetSnappingEnabled).toHaveBeenCalledWith(true)
  })
})

describe("snap events", () => {
  let rafCallback: FrameRequestCallback | null = null

  beforeEach(() => {
    rafCallback = null
    vi.stubGlobal(
      "requestAnimationFrame",
      vi.fn((cb: FrameRequestCallback) => {
        rafCallback = cb
        return 1
      }),
    )
    vi.stubGlobal("cancelAnimationFrame", vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function enableSnapping(): void {
    mockIsSnappingEnabled.mockReturnValue(false)
    mod.enableSnapping()
  }

  it("schedules processSnapMove via RAF on mousemove", () => {
    enableSnapping()
    const container = mockCtx.map.getContainer()

    container.dispatchEvent(new MouseEvent("mousemove", { clientX: 50, clientY: 60 }))

    expect(useSnapStore().snapPendingCoords).toEqual({ x: 50, y: 60 })
    expect(rafCallback).toBeDefined()
  })

  it("does not schedule a second RAF while one is pending", () => {
    enableSnapping()
    const container = mockCtx.map.getContainer()

    container.dispatchEvent(new MouseEvent("mousemove", { clientX: 50, clientY: 60 }))
    container.dispatchEvent(new MouseEvent("mousemove", { clientX: 70, clientY: 80 }))

    expect(useSnapStore().snapPendingCoords).toEqual({ x: 70, y: 80 })
    expect(vi.mocked(requestAnimationFrame)).toHaveBeenCalledTimes(1)
  })

  it("skips processing while the snap is frozen", () => {
    enableSnapping()
    const store = useSnapStore()
    store.snapFrozen = true

    mockCtx.map
      .getContainer()
      .dispatchEvent(new MouseEvent("mousemove", { clientX: 10, clientY: 20 }))
    rafCallback!(0)

    expect(store.snapPendingCoords).toBeNull()
    expect(mockFindNearestSnap).not.toHaveBeenCalled()
  })

  it("activates snap and shows the indicator when a snap is found", () => {
    mockFindNearestSnap.mockReturnValue({ lat: 36.5, lng: 127.5, type: "vertex", distance: 5 })
    enableSnapping()

    mockCtx.map
      .getContainer()
      .dispatchEvent(new MouseEvent("mousemove", { clientX: 100, clientY: 200 }))
    expect(rafCallback).toBeDefined()
    rafCallback!(0)

    const store = useSnapStore()
    expect(store.snapActive).toBe(true)
    expect(store.snapLatLng).toEqual({ lat: 36.5, lng: 127.5 })
    expect(mockCtx.map.getCanvas().style.cursor).toBe("crosshair")
    expect(window.__narsSnapLatLng).toEqual({ lat: 36.5, lng: 127.5 })
  })

  it("clears snap when none is found", () => {
    mockFindNearestSnap.mockReturnValue(null)
    enableSnapping()

    mockCtx.map
      .getContainer()
      .dispatchEvent(new MouseEvent("mousemove", { clientX: 100, clientY: 200 }))
    rafCallback!(0)

    const store = useSnapStore()
    expect(store.snapActive).toBe(false)
    expect(store.snapLatLng).toBeNull()
    expect(window.__narsSnapLatLng).toBeNull()
  })

  it("deactivates snap during edit mode when not dragging", () => {
    mockFindNearestSnap.mockReturnValue({ lat: 36.5, lng: 127.5, type: "vertex", distance: 5 })
    enableSnapping()
    const store = useSnapStore()
    store.editModeActive = true
    store.snapActive = true
    store.snapLatLng = { lat: 1, lng: 2 }

    mockCtx.map
      .getContainer()
      .dispatchEvent(new MouseEvent("mousemove", { clientX: 100, clientY: 200 }))
    rafCallback!(0)

    expect(store.snapActive).toBe(false)
    expect(store.snapLatLng).toBeNull()
  })

  it("returns early when there are no active snap phases", () => {
    mockFindNearestSnap.mockReturnValue({ lat: 1, lng: 2, type: "vertex", distance: 1 })
    enableSnapping()
    useAppStore().currentPhase = 2

    mockCtx.map
      .getContainer()
      .dispatchEvent(new MouseEvent("mousemove", { clientX: 100, clientY: 200 }))
    rafCallback!(0)

    expect(mockFindNearestSnap).not.toHaveBeenCalled()
  })

  it("freezes snap on mousedown and unfreezes on mouseup", () => {
    enableSnapping()
    const store = useSnapStore()
    store.snapActive = true
    store.snapLatLng = { lat: 36.5, lng: 127.5 }
    const container = mockCtx.map.getContainer()

    container.dispatchEvent(new MouseEvent("mousedown"))
    expect(store.snapFrozen).toBe(true)

    container.dispatchEvent(new MouseEvent("mouseup"))
    expect(store.snapFrozen).toBe(false)
  })
})
