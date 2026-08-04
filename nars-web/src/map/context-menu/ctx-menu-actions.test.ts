import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

const {
  mockApiFetch,
  mockShowToast,
  mockShowConfirm,
  mockGetUserMessageKey,
  mockRecordDelete,
  mockEnableEditMode,
  mockComputeCircleRing,
  mockCloseRing,
  mockDebugError,
  mockUpdateEndpointMarkers,
} = vi.hoisted(() => ({
  mockApiFetch: vi.fn(),
  mockShowToast: vi.fn(),
  mockShowConfirm: vi.fn(),
  mockGetUserMessageKey: vi.fn(() => "err_unknown"),
  mockRecordDelete: vi.fn(),
  mockEnableEditMode: vi.fn(),
  mockComputeCircleRing: vi.fn(
    () =>
      [
        [0, 0],
        [1, 1],
      ] as [number, number][],
  ),
  mockCloseRing: vi.fn((ring) => ring),
  mockDebugError: vi.fn(),
  mockUpdateEndpointMarkers: vi.fn(),
}))

vi.mock("../../api", () => ({ apiFetch: mockApiFetch }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast, showConfirm: mockShowConfirm }))
vi.mock("../../lib/errors", () => ({ getUserMessageKey: mockGetUserMessageKey }))
vi.mock("../undo", () => ({ recordDelete: mockRecordDelete }))
vi.mock("../draw/draw-events", () => ({ enableEditMode: mockEnableEditMode }))
vi.mock("../rendering/geometry", () => ({
  computeCircleRing: mockComputeCircleRing,
  closeRing: mockCloseRing,
}))
vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))
vi.mock("../roads/road-directions", () => ({ updateEndpointMarkers: mockUpdateEndpointMarkers }))

let _setCtx: (ctx: any) => void
let mockFeaturesStoreRemove: ReturnType<typeof vi.fn>
let mod: any
let useLayerStore: any
let useAppStore: any
let useSelectionStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())

  const stateMod = await import("../core/state")
  _setCtx = stateMod._setCtx
  mockFeaturesStoreRemove = vi.fn()
  vi.doMock("../../stores/featuresStore", () => ({
    useFeaturesStore: () => ({
      getAll: vi.fn().mockReturnValue([]),
      add: vi.fn(),
      remove: mockFeaturesStoreRemove,
      update: vi.fn(),
    }),
  }))
  _setCtx({ featuresSource: { setData: vi.fn() } } as any)

  mod = await import("./ctx-menu-actions")

  const ls = await import("../../stores/layerStore")
  useLayerStore = ls.useLayerStore
  const as = await import("../../stores/appStore")
  useAppStore = as.useAppStore
  const ss = await import("../../stores/selectionStore")
  useSelectionStore = ss.useSelectionStore
})

function addLayerEntry(phaseKey: string, overrides: Record<string, any> = {}): Record<string, any> {
  const dbId = overrides.dbId || "1"
  const entry = {
    id: `feat_${dbId}`,
    dbId,
    type: "geojson",
    ...overrides,
    data: {
      type: "areas",
      label: "Test",
      coordinates: [{ lat: 36.0, lng: 127.0 }],
      ...(overrides.data || {}),
    },
  }
  useLayerStore().addFeature(phaseKey, entry)
  return entry
}

describe("findLayerEntryByDbId", () => {
  it("returns null when dbId not found", () => {
    expect(mod.findLayerEntryByDbId("nonexistent")).toBeNull()
  })

  it("finds entry by dbId", () => {
    const entry = addLayerEntry("areas", { dbId: "42" })
    expect(mod.findLayerEntryByDbId("42")).toEqual(entry)
  })
})

describe("enableEditGeometry", () => {
  it("shows info toast when another feature is already selected", () => {
    useSelectionStore().selectFeature("other")
    addLayerEntry("areas", { dbId: "myfeature" })

    mod.enableEditGeometry("myfeature")

    expect(mockShowToast).toHaveBeenCalledWith("map_select_feature_first", "info")
  })

  it("selects feature when none is selected", () => {
    addLayerEntry("areas", { dbId: "myfeature" })

    mod.enableEditGeometry("myfeature")

    expect(useSelectionStore().selectedFeatureDbId).toBe("myfeature")
  })

  it("shows error toast when entry not found", () => {
    mod.enableEditGeometry("nonexistent")

    expect(mockShowToast).toHaveBeenCalledWith("map_feature_not_found", "error")
  })

  it("delegates to editFeatureInfo for circle type", () => {
    addLayerEntry("cityCenter", {
      dbId: "circle1",
      type: "circle",
      data: { type: "cityCenter", label: "CC", radius: 100, lat: 36.0, lng: 127.0 },
    })

    mod.enableEditGeometry("circle1")

    expect(mockShowToast).not.toHaveBeenCalledWith("map_edit_mode_unavailable", "error")
  })

  it("shows error when geoman not available", () => {
    addLayerEntry("areas", { dbId: "g1" })

    mod.enableEditGeometry("g1")

    expect(mockShowToast).toHaveBeenCalledWith("map_edit_mode_unavailable", "error")
  })

  it("enables edit mode when geoman is available", () => {
    _setCtx({ geoman: {} as any, featuresSource: { setData: vi.fn() } } as any)
    addLayerEntry("areas", { dbId: "g1" })

    mod.enableEditGeometry("g1")

    expect(mockEnableEditMode).toHaveBeenCalledWith("feat_g1")
    expect(mockShowToast).toHaveBeenCalledWith("map_edit_mode_hint", "info")
  })
})

describe("editFeatureInfo", () => {
  it("shows error when entry not found", async () => {
    await mod.editFeatureInfo("nonexistent")

    expect(mockShowToast).toHaveBeenCalledWith("map_feature_not_found", "error")
  })

  it("returns early for houseEntrances type", async () => {
    addLayerEntry("houseEntrances", { dbId: "he1", data: { type: "houseEntrances", label: "H1" } })

    await mod.editFeatureInfo("he1")

    expect(mockShowToast).not.toHaveBeenCalled()
  })
})

describe("removeFeature", () => {
  it("shows error when entry not found", async () => {
    await mod.removeFeature("nonexistent")

    expect(mockShowToast).toHaveBeenCalledWith("map_feature_not_found", "error")
  })

  it("returns early when confirm is denied", async () => {
    addLayerEntry("areas", { dbId: "d1", data: { type: "areas", label: "Denied" } })
    mockShowConfirm.mockResolvedValue(false)

    await mod.removeFeature("d1")

    expect(mockRecordDelete).not.toHaveBeenCalled()
  })

  it("deletes feature and calls cleanup hooks", async () => {
    addLayerEntry("areas", { dbId: "del1", data: { type: "areas", label: "Delete Me" } })
    mockShowConfirm.mockResolvedValue(true)
    mockApiFetch.mockResolvedValue({ ok: true })

    await mod.removeFeature("del1")

    expect(mockRecordDelete).toHaveBeenCalled()
    expect(mockApiFetch).toHaveBeenCalledWith("/api/features/del1", { method: "DELETE" })
    expect(mockFeaturesStoreRemove).toHaveBeenCalledWith("feat_del1")
    expect(mockShowToast).toHaveBeenCalledWith("map_feature_deleted", "success")
  })

  it("does not record undo when DELETE fails", async () => {
    addLayerEntry("areas", { dbId: "fail1", data: { type: "areas", label: "Fail" } })
    mockShowConfirm.mockResolvedValue(true)
    mockApiFetch.mockRejectedValue(new Error("Network failure"))

    await mod.removeFeature("fail1")

    expect(mockRecordDelete).not.toHaveBeenCalled()
    expect(mockFeaturesStoreRemove).not.toHaveBeenCalled()
    expect(mockShowToast).toHaveBeenCalledWith("map_delete_failed", expect.any(String))
  })

  it("city-center state derives from the layer store after delete", async () => {
    addLayerEntry("cityCenter", { dbId: "cc1", data: { type: "cityCenter", label: "CC" } })
    expect(useAppStore().cityCenterMode).toBe("city_center")
    mockShowConfirm.mockResolvedValue(true)
    mockApiFetch.mockResolvedValue({ ok: true })

    await mod.removeFeature("cc1")

    expect(useAppStore().cityCenterMode).toBeNull()
    expect(useAppStore().cityCenterLatLng).toBeNull()
  })

  it("calls updateEndpointMarkers on road delete", async () => {
    addLayerEntry("roads", { dbId: "rd1", data: { type: "roads", label: "Road" } })
    mockShowConfirm.mockResolvedValue(true)
    mockApiFetch.mockResolvedValue({ ok: true })

    await mod.removeFeature("rd1")

    expect(mockUpdateEndpointMarkers).toHaveBeenCalled()
  })
})
