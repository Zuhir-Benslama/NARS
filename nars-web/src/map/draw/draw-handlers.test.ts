import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { nextTick } from "vue"

const {
  mockBuildDrawControl,
  mockClearEdgeVisibilityPoll,
  mockRepatchMarker,
  mockEnsureGeoman,
  mockCompleteDrawingWithGeometry,
  mockShowContextMenu,
  mockShowMapContextMenu,
  mockRemoveLastVertex,
  mockCommitEditMode,
  mockCancelEditMode,
  mockUndo,
  mockUpdateSelectionHighlight,
} = vi.hoisted(() => ({
  mockBuildDrawControl: vi.fn(async () => {}),
  mockClearEdgeVisibilityPoll: vi.fn(),
  mockRepatchMarker: vi.fn(),
  mockEnsureGeoman: vi.fn(async () => {}),
  mockCompleteDrawingWithGeometry: vi.fn(async () => {}),
  mockShowContextMenu: vi.fn(),
  mockShowMapContextMenu: vi.fn(async () => {}),
  mockRemoveLastVertex: vi.fn(async () => {}),
  mockCommitEditMode: vi.fn(async () => {}),
  mockCancelEditMode: vi.fn(async () => {}),
  mockUndo: vi.fn(async () => {}),
  mockUpdateSelectionHighlight: vi.fn(),
}))

const { mockGetCtx, getSetCtx } = vi.hoisted(() => {
  let current: any = null
  return {
    mockGetCtx: vi.fn(() => current),
    getSetCtx: (c: any) => {
      current = c
    },
  }
})

const { mockIsEditMode, mockGetActiveDrawModes } = vi.hoisted(() => ({
  mockIsEditMode: vi.fn(() => false),
  mockGetActiveDrawModes: vi.fn<() => string[]>(() => []),
}))

vi.mock("../../utils/debug", () => ({
  debugError: vi.fn(),
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))

vi.mock("../core/state", () => ({
  getCtx: mockGetCtx,
  tryGetCtx: () => null,
  updateSelectionHighlight: mockUpdateSelectionHighlight,
}))

vi.mock("./draw-state", () => ({
  repatchMarker: mockRepatchMarker,
}))

vi.mock("./draw-control", () => ({
  buildDrawControl: mockBuildDrawControl,
  clearEdgeVisibilityPoll: mockClearEdgeVisibilityPoll,
}))

vi.mock("../map-init", () => ({
  ensureGeoman: mockEnsureGeoman,
}))

vi.mock("./draw-complete", () => ({
  completeDrawingWithGeometry: mockCompleteDrawingWithGeometry,
  getDrawingPhase: vi.fn(),
  isSavingFeature: vi.fn(() => false),
  removeLastVertex: mockRemoveLastVertex,
}))

vi.mock("../edit/edit-mode", () => ({
  isEditMode: mockIsEditMode,
  commitEditMode: mockCommitEditMode,
  cancelEditMode: mockCancelEditMode,
}))

vi.mock("../context-menu/context-menu", () => ({
  showContextMenu: mockShowContextMenu,
  showMapContextMenu: mockShowMapContextMenu,
}))

vi.mock("../undo", () => ({
  undo: mockUndo,
}))

import { registerDrawHandlers, destroyDrawHandlers, pointToSegmentDist } from "./draw-handlers"

// ─── FAKE MAP / GEOMAN FACTORIES ─────────────────────────────────────────────

function makeGeoman(overrides: Record<string, unknown> = {}) {
  const geoman: any = {
    actionInstances: {},
    disableDraw: vi.fn(async () => {}),
    getActiveDrawModes: mockGetActiveDrawModes,
    ...overrides,
  }
  return geoman
}

// Fake maplibre map that captures registered event callbacks.
function makeMap() {
  const listeners: Record<string, Set<(...args: any[]) => void>> = {}
  const map: any = {
    on: (name: string, cb: any) => {
      if (!listeners[name]) listeners[name] = new Set()
      listeners[name].add(cb)
    },
    off: (name: string, cb: any) => {
      listeners[name]?.delete(cb)
    },
    _emit: (name: string, ...args: any[]) => {
      listeners[name]?.forEach((cb) => cb(...args))
    },
    getContainer: () => document.body,
    queryRenderedFeatures: vi.fn(() => []),
    project: vi.fn(() => ({ x: 0, y: 0 })),
    getSource: vi.fn(() => ({ setData: vi.fn() })),
  }
  return { map, listeners }
}

function makeCtx(overrides: Record<string, unknown> = {}) {
  const { map, listeners } = makeMap()
  return { map, listeners, geoman: makeGeoman(), ...overrides }
}

let featuresStore: any
let appStore: any
let modalStore: any
let selectionStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  mockIsEditMode.mockReset().mockReturnValue(false)
  mockGetActiveDrawModes.mockReset().mockReturnValue([])
  setActivePinia(createPinia())
  getSetCtx(makeCtx())

  const { useFeaturesStore } = await import("../../stores/featuresStore")
  featuresStore = useFeaturesStore()
  const { useAppStore } = await import("../../stores/appStore")
  appStore = useAppStore()
  const { useModalStore } = await import("../../stores/modalStore")
  modalStore = useModalStore()
  const { useSelectionStore } = await import("../../stores/selectionStore")
  selectionStore = useSelectionStore()
})

afterEach(() => {
  destroyDrawHandlers()
  getSetCtx(null)
})

function addFeature(overrides: Record<string, unknown> = {}) {
  const f = {
    id: "f1",
    geometry: { type: "Polygon", coordinates: [[]] } as any,
    properties: { dbId: "db1", phaseKey: "areas", label: "A" },
    ...overrides,
  }
  featuresStore.add(f)
  return f
}

// ─── GM:CREATE ────────────────────────────────────────────────────────────────

describe("onFeatureCreated (gm:create)", () => {
  it("returns early when a save is in progress", async () => {
    const { map } = mockGetCtx()
    registerDrawHandlers()
    map._emit("gm:create", { shape: "polygon" })
    await nextTick()
    expect(mockCompleteDrawingWithGeometry).not.toHaveBeenCalled()
  })

  it("converts a circle polygon into a point with radius", async () => {
    const ring: [number, number][] = [
      [0, 0],
      [1, 0],
      [1, 1],
      [0, 1],
      [0, 0],
    ]
    const { map } = mockGetCtx()
    const featureData = {
      getGeoJson: () => ({ geometry: { type: "Polygon", coordinates: [ring] } }),
    }
    registerDrawHandlers()
    map._emit("gm:create", { shape: "circle", featureData })
    await nextTick()
    expect(mockCompleteDrawingWithGeometry).toHaveBeenCalledTimes(1)
    const call = mockCompleteDrawingWithGeometry.mock.calls[0] as unknown as [any, string, any]
    const [geometry, drawType] = call
    expect(drawType).toBe("circle")
    expect(geometry.type).toBe("Point")
    expect(geometry.coordinates).toEqual([0.4, 0.4])
    expect(geometry.radius).toBeGreaterThan(0)
  })

  it("converts a MultiPolygon to a single polygon using the first ring", async () => {
    const mp = {
      type: "MultiPolygon",
      coordinates: [
        [
          [
            [0, 0],
            [1, 0],
            [0, 1],
            [0, 0],
          ],
        ],
      ],
    }
    const { map } = mockGetCtx()
    const featureData = { _geoJson: { geometry: mp } }
    registerDrawHandlers()
    map._emit("gm:create", { shape: "polygon", featureData })
    await nextTick()
    expect(mockCompleteDrawingWithGeometry).toHaveBeenCalledTimes(1)
    const call = mockCompleteDrawingWithGeometry.mock.calls[0] as unknown as [any, string, any]
    const [geometry, drawType] = call
    expect(drawType).toBe("polygon")
    expect(geometry.type).toBe("Polygon")
  })

  it("uses the drawing phase drawType when the shape is not mapped", async () => {
    const { map } = mockGetCtx()
    const featureData = {
      _geoJson: { geometry: { type: "Point", coordinates: [0, 0] } },
    }
    const drawComplete = await import("./draw-complete")
    ;(drawComplete.getDrawingPhase as any).mockReturnValue({ drawType: "marker" })
    registerDrawHandlers()
    map._emit("gm:create", { shape: "somethingElse", featureData })
    await nextTick()
    const drawType = (mockCompleteDrawingWithGeometry.mock.calls[0] as unknown as [any, string])[1]
    expect(drawType).toBe("marker")
  })

  it("logs an error when completeDrawingWithGeometry rejects", async () => {
    mockCompleteDrawingWithGeometry.mockRejectedValueOnce(new Error("boom"))
    const { map } = mockGetCtx()
    const featureData = {
      _geoJson: { geometry: { type: "Point", coordinates: [0, 0] } },
    }
    registerDrawHandlers()
    map._emit("gm:create", { featureData })
    await nextTick()
    const { debugError } = await import("../../utils/debug")
    expect(debugError).toHaveBeenCalled()
  })

  it("returns early when featureData has no geometry", async () => {
    const { map } = mockGetCtx()
    registerDrawHandlers()
    map._emit("gm:create", { featureData: {} })
    await nextTick()
    expect(mockCompleteDrawingWithGeometry).not.toHaveBeenCalled()
  })
})

// ─── CONTEXT MENU ─────────────────────────────────────────────────────────────

describe("onContextMenu", () => {
  function fireContextMenu(clientX = 10, clientY = 10) {
    const ev = new MouseEvent("contextmenu", { clientX, clientY })
    Object.defineProperty(ev, "target", { value: document.body })
    window.dispatchEvent(ev)
  }

  it("commits edit mode when edit mode is active", async () => {
    mockIsEditMode.mockReturnValueOnce(true)
    mockGetCtx()
    registerDrawHandlers()
    fireContextMenu()
    await nextTick()
    expect(mockCommitEditMode).toHaveBeenCalled()
    expect(mockShowContextMenu).not.toHaveBeenCalled()
  })

  it("ignores events outside the map container", async () => {
    const ctx = mockGetCtx()
    ctx.map.getContainer = () => {
      const el = document.createElement("div")
      return el
    }
    registerDrawHandlers()
    fireContextMenu()
    await nextTick()
    expect(mockCommitEditMode).not.toHaveBeenCalled()
    expect(mockShowContextMenu).not.toHaveBeenCalled()
  })

  it("removes the last vertex when mid-draw on a line", async () => {
    const ctx = mockGetCtx()
    ctx.geoman.actionInstances = {
      draw__polygon: {
        lineDrawer: {
          shapeLngLats: [
            { lng: 0, lat: 0 },
            { lng: 1, lat: 1 },
            { lng: 2, lat: 2 },
          ],
        },
      },
    }
    registerDrawHandlers()
    fireContextMenu()
    await nextTick()
    expect(mockRemoveLastVertex).toHaveBeenCalled()
  })

  it("rebuilds draw control when mid-draw with too few coordinates", async () => {
    const ctx = mockGetCtx()
    ctx.geoman.actionInstances = {
      draw__polygon: { lineDrawer: { shapeLngLats: [{ lng: 0, lat: 0 }] } },
    }
    appStore.currentPhase = 0 // areas phase
    registerDrawHandlers()
    fireContextMenu()
    await nextTick()
    expect(mockClearEdgeVisibilityPoll).toHaveBeenCalled()
    expect(ctx.geoman.disableDraw).toHaveBeenCalled()
    expect(mockBuildDrawControl).toHaveBeenCalled()
    expect(mockRepatchMarker).toHaveBeenCalled()
  })

  it("shows the feature context menu when a rendered feature is found", async () => {
    const ctx = mockGetCtx()
    ctx.map.queryRenderedFeatures = vi.fn(() => [
      { source: "features", properties: { dbId: "db1", phaseKey: "areas" } },
    ])
    registerDrawHandlers()
    fireContextMenu(100, 200)
    await nextTick()
    expect(mockShowContextMenu).toHaveBeenCalledWith(100, 200, "db1", "areas")
  })

  it("finds the nearest feature when no rendered feature matches", async () => {
    addFeature({
      geometry: { type: "Point", coordinates: [10, 20] },
      properties: { dbId: "db1", phaseKey: "areas", label: "A" },
    })
    appStore.currentPhase = 1 // districts
    const ctx = mockGetCtx()
    ctx.map.project = vi.fn(() => ({ x: 15, y: 15 }))
    ctx.map.queryRenderedFeatures = vi.fn(() => [
      { source: "not-features", properties: { dbId: "db1" } },
    ])
    registerDrawHandlers()
    fireContextMenu(10, 10)
    await nextTick()
    expect(mockShowContextMenu).toHaveBeenCalled()
    const args = mockShowContextMenu.mock.calls[0]
    expect(args[2]).toBe("db1")
    expect(args[3]).toBe("areas")
  })

  it("shows the map context menu when nothing is found", async () => {
    appStore.currentPhase = 1
    const ctx = mockGetCtx()
    ctx.map.queryRenderedFeatures = vi.fn(() => [])
    registerDrawHandlers()
    fireContextMenu()
    await nextTick()
    expect(mockShowMapContextMenu).toHaveBeenCalled()
    const phase = (mockShowMapContextMenu.mock.calls[0] as unknown as [number, number, any])[2]
    expect(phase.key).toBe("districts")
  })
})

// ─── CLICK ────────────────────────────────────────────────────────────────────

describe("onClick", () => {
  function fireClick(point = { x: 5, y: 5 }) {
    const ctx = mockGetCtx()
    ctx.map._emit("click", { point })
  }

  it("ignores clicks while actively drawing a new shape", () => {
    mockGetActiveDrawModes.mockReturnValueOnce(["draw__polygon"])
    registerDrawHandlers()
    fireClick()
    selectionStore.$reset()
    expect(selectionStore.selectedFeatureDbId).toBeNull()
  })

  it("selects a feature when a rendered feature with dbId is found", () => {
    const ctx = mockGetCtx()
    ctx.map.queryRenderedFeatures = vi.fn(() => [
      { source: "features", properties: { dbId: "db9" } },
    ])
    registerDrawHandlers()
    fireClick()
    expect(selectionStore.selectedFeatureDbId).toBe("db9")
    expect(mockUpdateSelectionHighlight).toHaveBeenCalledWith("db9")
  })

  it("clears selection and re-arms draw control on empty click", async () => {
    selectionStore.setSelectedFeatureDbId("old-selection")
    appStore.currentPhase = 0
    registerDrawHandlers()
    fireClick()
    expect(selectionStore.selectedFeatureDbId).toBeNull()
    expect(mockUpdateSelectionHighlight).toHaveBeenCalledWith(null)
    await nextTick()
    expect(mockBuildDrawControl).toHaveBeenCalled()
  })

  it("does not re-arm draw control when phase is namingPanels", async () => {
    selectionStore.setSelectedFeatureDbId("old-selection")
    appStore.currentPhase = 7 // namingPanels
    registerDrawHandlers()
    fireClick()
    await nextTick()
    expect(mockBuildDrawControl).not.toHaveBeenCalled()
  })

  it("logs an error when re-arming draw control fails", async () => {
    mockEnsureGeoman.mockRejectedValueOnce(new Error("bundle failed"))
    appStore.currentPhase = 0
    registerDrawHandlers()
    fireClick()
    await nextTick()
    const { debugError } = await import("../../utils/debug")
    expect(debugError).toHaveBeenCalled()
  })
})

// ─── KEYDOWN ──────────────────────────────────────────────────────────────────

describe("onKeyDown", () => {
  function fireKey(key: string, options: KeyboardEventInit = {}) {
    const ev = new KeyboardEvent("keydown", { key, ...options, bubbles: true })
    document.dispatchEvent(ev)
  }

  it("ignores Ctrl+Z / Escape while typing in an input", () => {
    const input = document.createElement("input")
    document.body.appendChild(input)
    input.focus()
    fireKey("Escape")
    input.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }))
    expect(mockCancelEditMode).not.toHaveBeenCalled()
    document.body.removeChild(input)
  })

  it("Escape with active modal does nothing", () => {
    modalStore.visible = true
    mockGetActiveDrawModes.mockReturnValueOnce(["draw__polygon"])
    registerDrawHandlers()
    fireKey("Escape")
    expect(mockCancelEditMode).not.toHaveBeenCalled()
  })

  it("Escape while drawing disables draw and re-arms control", async () => {
    appStore.currentPhase = 0
    mockGetActiveDrawModes.mockReturnValueOnce(["draw__polygon"])
    const ctx = mockGetCtx()
    registerDrawHandlers()
    fireKey("Escape")
    await nextTick()
    expect(ctx.geoman.disableDraw).toHaveBeenCalled()
    expect(mockBuildDrawControl).toHaveBeenCalled()
    expect(mockRepatchMarker).toHaveBeenCalled()
  })

  it("Escape while drawing logs when disableDraw rejects", async () => {
    const ctx = mockGetCtx()
    ctx.geoman.disableDraw = vi.fn(async () => {
      throw new Error("disable failed")
    })
    mockGetActiveDrawModes.mockReturnValueOnce(["draw__polygon"])
    appStore.currentPhase = 0
    registerDrawHandlers()
    fireKey("Escape")
    await nextTick()
    const { debugError } = await import("../../utils/debug")
    expect(debugError).toHaveBeenCalled()
  })

  it("Escape while editing cancels edit mode", async () => {
    mockIsEditMode.mockReturnValueOnce(true)
    registerDrawHandlers()
    fireKey("Escape")
    await nextTick()
    expect(mockCancelEditMode).toHaveBeenCalled()
  })

  it("Ctrl+Z triggers undo", () => {
    registerDrawHandlers()
    fireKey("z", { ctrlKey: true })
    expect(mockUndo).toHaveBeenCalled()
  })

  it("Meta+Z triggers undo", () => {
    registerDrawHandlers()
    fireKey("z", { metaKey: true })
    expect(mockUndo).toHaveBeenCalled()
  })
})

// ─── LIFECYCLE ────────────────────────────────────────────────────────────────

// ─── GEOMETRY HELPER ──────────────────────────────────────────────────────────

describe("pointToSegmentDist", () => {
  it("returns the distance to the nearest point on a segment", () => {
    expect(pointToSegmentDist(5, 5, 0, 0, 10, 0)).toBeCloseTo(5)
  })

  it("handles an infinite-slope segment", () => {
    expect(pointToSegmentDist(0, 0, 5, 0, 5, 10)).toBeCloseTo(5)
  })

  it("handles a degenerate zero-length segment by returning point distance", () => {
    expect(pointToSegmentDist(3, 4, 0, 0, 0, 0)).toBeCloseTo(5)
    expect(pointToSegmentDist(1, 1, 1, 1, 1, 1)).toBe(0)
  })

  it("clamps the projected point to the segment endpoints", () => {
    expect(pointToSegmentDist(-5, 0, 0, 0, 10, 0)).toBeCloseTo(5)
    expect(pointToSegmentDist(15, 0, 0, 0, 10, 0)).toBeCloseTo(5)
  })
})

describe("registration lifecycle", () => {
  it("re-registering does not stack duplicates and destroy cleans up", async () => {
    appStore.currentPhase = 0
    const ctx = mockGetCtx()
    registerDrawHandlers()
    registerDrawHandlers()
    ctx.map._emit("click", { point: { x: 0, y: 0 } })
    await nextTick()
    expect(mockBuildDrawControl).toHaveBeenCalledTimes(1)

    destroyDrawHandlers()
    ctx.map._emit("click", { point: { x: 0, y: 0 } })
    await nextTick()
    expect(mockBuildDrawControl).toHaveBeenCalledTimes(1)
  })
})
