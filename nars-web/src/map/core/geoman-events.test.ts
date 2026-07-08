import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

const {
  mockApiFetch,
  mockSetEditDragActive,
  mockGetActiveSnapPhases,
  mockSnapPointForEdit,
  mockDisableEditMode,
  mockGetActiveEditEntry,
  mockIsEditMode,
  mockRecordDelete,
  mockRefreshLayerVisibility,
  mockGetErrorMessage,
  mockShowToast,
  mockDebugError,
} = vi.hoisted(() => ({
  mockApiFetch: vi.fn(),
  mockSetEditDragActive: vi.fn(),
  mockGetActiveSnapPhases: vi.fn(() => []),
  mockSnapPointForEdit: vi.fn(),
  mockDisableEditMode: vi.fn(),
  mockGetActiveEditEntry: vi.fn(() => null),
  mockIsEditMode: vi.fn(() => false),
  mockRecordDelete: vi.fn(),
  mockRefreshLayerVisibility: vi.fn(),
  mockGetErrorMessage: vi.fn((e) => `err:${e.message}`),
  mockShowToast: vi.fn(),
  mockDebugError: vi.fn(),
}))

vi.mock("../../api", () => ({ apiFetch: mockApiFetch }))
vi.mock("../snapping/snapping", () => ({
  setEditDragActive: mockSetEditDragActive,
  getActiveSnapPhases: mockGetActiveSnapPhases,
  snapPointForEdit: mockSnapPointForEdit,
}))
vi.mock("../edit/edit-mode", () => ({
  disableEditMode: mockDisableEditMode,
  getActiveEditEntry: mockGetActiveEditEntry,
  isEditMode: mockIsEditMode,
}))
vi.mock("../undo", () => ({ recordDelete: mockRecordDelete }))
vi.mock("../rendering/labels", () => ({ refreshLayerVisibility: mockRefreshLayerVisibility }))
vi.mock("../../lib/errors", () => ({ getErrorMessage: mockGetErrorMessage }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast }))
vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))

import { useLayerStore } from "../../stores/layerStore"

let _setCtx: (ctx: any) => void
let featuresStore: any
let mod: typeof import("./geoman-events")
let mapOn: ReturnType<typeof vi.fn>
let useEditStore: any

function getHandler(eventName: string): (...args: any[]) => any {
  return mapOn.mock.calls.find(([name]: [string]) => name === eventName)?.[1]
}

function addLayerEntry(phaseKey: string, overrides: Record<string, any> = {}): void {
  const dbId = overrides.dbId || "1"
  const entry: any = {
    id: `feat_${dbId}`,
    dbId,
    data: { type: phaseKey, label: "Test", coordinates: [{ lat: 36.0, lng: 127.0 }] },
    type: "geojson",
    ...overrides,
    data: { type: phaseKey, label: "Test", coordinates: [{ lat: 36.0, lng: 127.0 }], ...(overrides.data || {}) },
  }
  useLayerStore().addFeature(phaseKey as any, entry)
}

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())

  const stateMod = await import("./state")
  _setCtx = stateMod._setCtx
  featuresStore = stateMod.featuresStore

  mapOn = vi.fn()
  _setCtx({
    map: { on: mapOn } as any,
    geoman: {} as any,
    featuresSource: { setData: vi.fn() } as any,
  })

  mod = await import("./geoman-events")

  const es = await import("../../stores/editStore")
  useEditStore = es.useEditStore
})

describe("registerGeomanEvents", () => {
  it("binds all five event handlers when geoman is available", () => {
    mod.registerGeomanEvents()

    expect(mapOn).toHaveBeenCalledWith("pm:markerdragstart", expect.any(Function))
    expect(mapOn).toHaveBeenCalledWith("pm:markerdragend", expect.any(Function))
    expect(mapOn).toHaveBeenCalledWith("dblclick", expect.any(Function))
    expect(mapOn).toHaveBeenCalledWith("gm:editend", expect.any(Function))
    expect(mapOn).toHaveBeenCalledWith("gm:remove", expect.any(Function))
  })

  it("debugError when geoman is not initialized", () => {
    _setCtx({ map: { on: vi.fn() } as any, geoman: undefined } as any)

    mod.registerGeomanEvents()

    expect(mockDebugError).toHaveBeenCalledWith("Geoman not initialized")
  })
})

describe("onVertexDragStart", () => {
  it("sets drag active and stores markerIndex", () => {
    mod.registerGeomanEvents()
    const handler = getHandler("pm:markerdragstart")
    handler({ markerIndex: 3, vertexIndex: undefined })

    expect(mockSetEditDragActive).toHaveBeenCalledWith(true)
    expect(useEditStore().draggedVertexIndex).toBe(3)
  })

  it("falls back to vertexIndex when markerIndex is undefined", () => {
    mod.registerGeomanEvents()
    const handler = getHandler("pm:markerdragstart")
    handler({ vertexIndex: 5 })

    expect(useEditStore().draggedVertexIndex).toBe(5)
  })
})

describe("onVertexDragEnd", () => {
  it("clears drag state", () => {
    useEditStore().draggedVertexIndex = 42
    mod.registerGeomanEvents()
    const handler = getHandler("pm:markerdragend")
    handler()

    expect(mockSetEditDragActive).toHaveBeenCalledWith(false)
    expect(useEditStore().draggedVertexIndex).toBeNull()
  })
})

describe("onEditEnd", () => {
  it("updates point geometry coordinates", () => {
    mockGetActiveEditEntry.mockReturnValue({ id: "feat_1", data: { lat: 0, lng: 0 } })
    mod.registerGeomanEvents()
    const handler = getHandler("gm:editend")

    handler({ feature: { _geoJson: { geometry: { type: "Point", coordinates: [10, 20] } } } })

    expect(mockGetActiveEditEntry().data.lat).toBe(20)
    expect(mockGetActiveEditEntry().data.lng).toBe(10)
  })

  it("updates line geometry coordinates", () => {
    const entry = { id: "feat_1", data: { coordinates: [] } }
    mockGetActiveEditEntry.mockReturnValue(entry)
    mod.registerGeomanEvents()
    const handler = getHandler("gm:editend")

    handler({
      feature: {
        _geoJson: {
          geometry: {
            type: "LineString",
            coordinates: [[127.0, 36.0], [127.1, 36.1]],
          },
        },
      },
    })

    expect(entry.data.coordinates).toEqual([
      { lat: 36.0, lng: 127.0 },
      { lat: 36.1, lng: 127.1 },
    ])
  })

  it("snaps dragged vertex when active snap phases exist", () => {
    mockGetActiveSnapPhases.mockReturnValue(["roads"])
    useEditStore().draggedVertexIndex = 0
    mockSnapPointForEdit.mockReturnValue({ lat: 37.0, lng: 128.0 })
    _setCtx({ map: { on: mapOn, project: vi.fn(() => ({ x: 100, y: 200 })) } as any, geoman: {} as any, featuresSource: { setData: vi.fn() } as any })

    const entry = { id: "feat_1", data: { coordinates: [] } }
    mockGetActiveEditEntry.mockReturnValue(entry)
    mod.registerGeomanEvents()
    const handler = getHandler("gm:editend")

    handler({
      feature: {
        _geoJson: {
          geometry: {
            type: "LineString",
            coordinates: [[127.0, 36.0]],
          },
        },
      },
    })

    expect(mockSnapPointForEdit).toHaveBeenCalled()
    expect(entry.data.coordinates[0].lat).toBe(37.0)
    expect(entry.data.coordinates[0].lng).toBe(128.0)
  })

  it("does nothing when no active edit entry", () => {
    mockGetActiveEditEntry.mockReturnValue(null)
    mod.registerGeomanEvents()
    const handler = getHandler("gm:editend")

    handler({ feature: { _geoJson: { geometry: { type: "Point", coordinates: [0, 0] } } } })

    expect(mockSetEditDragActive).not.toHaveBeenCalled()
  })
})

describe("onRemove", () => {
  it("shows error when feature has no dbId", async () => {
    mod.registerGeomanEvents()
    const handler = getHandler("gm:remove")

    await handler({ feature: { _geoJson: { properties: {} } } })

    expect(mockShowToast).toHaveBeenCalledWith("Cannot delete: feature ID not found", "error")
  })

  it("disables edit mode when active entry matches", async () => {
    mockGetActiveEditEntry.mockReturnValue({ dbId: "del42" })
    mod.registerGeomanEvents()
    const handler = getHandler("gm:remove")

    await handler({ feature: { _geoJson: { properties: { dbId: "del42" } } } })

    expect(mockDisableEditMode).toHaveBeenCalled()
  })

  it("removes layer entry when feature exists in store", async () => {
    addLayerEntry("areas", { dbId: "del1" })
    mockGetActiveEditEntry.mockReturnValue(null)
    mockApiFetch.mockResolvedValue({ ok: true })
    mod.registerGeomanEvents()
    const handler = getHandler("gm:remove")

    await handler({ feature: { _geoJson: { properties: { dbId: "del1" } } } })

    expect(mockApiFetch).toHaveBeenCalledWith("/api/features/del1", { method: "DELETE" })
    expect(featuresStore.getAll()).toHaveLength(0)
    expect(mockShowToast).toHaveBeenCalledWith("Feature deleted.", "success")
  })

  it("shows error on failed API response", async () => {
    addLayerEntry("areas", { dbId: "fail1" })
    mockGetActiveEditEntry.mockReturnValue(null)
    mockApiFetch.mockResolvedValue({ ok: false, status: 500 })
    mod.registerGeomanEvents()
    const handler = getHandler("gm:remove")

    await handler({ feature: { _geoJson: { properties: { dbId: "fail1" } } } })

    expect(mockShowToast).toHaveBeenCalledWith("Delete failed: HTTP 500", "error")
  })
})
