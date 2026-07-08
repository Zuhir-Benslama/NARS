import { describe, it, expect, vi, beforeEach } from "vitest"
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
  const mockContainer = document.createElement("div")
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
    resetMockDom: () => { mockCanvas.style.cursor = "" },
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
    useLayerStore().addFeature("areas", { id: "f1", dbId: "1", data: { type: "areas", label: "A" } as any, type: "geojson" })
    useLayerStore().addFeature("roads", { id: "f2", dbId: "2", data: { type: "roads", label: "R" } as any, type: "geojson" })

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
    mockCtx.map.on = (ev: string, fn: Function) => { handlers[ev] = fn }
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
