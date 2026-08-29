import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import type { LayerEntry } from "../../types"

const {
  mockEnsureGeoman,
  mockFindLayerEntry,
  mockSetActiveEditEntry,
  mockSetActiveEditCoordsSnapshot,
  mockSetActiveGeomanFeatureId,
  mockDisableCrosshair,
  mockDisableSnapping,
  mockSetSnapExclude,
  mockSetEditModeActive,
  mockDebugError,
  mockPatchMarkerPointerSnap,
  mockBuildGeomanImportFeature,
} = vi.hoisted(() => ({
  mockEnsureGeoman: vi.fn().mockResolvedValue(undefined),
  mockFindLayerEntry: vi.fn(),
  mockSetActiveEditEntry: vi.fn(),
  mockSetActiveEditCoordsSnapshot: vi.fn(),
  mockSetActiveGeomanFeatureId: vi.fn(),
  mockDisableCrosshair: vi.fn(),
  mockDisableSnapping: vi.fn(),
  mockSetSnapExclude: vi.fn(),
  mockSetEditModeActive: vi.fn(),
  mockDebugError: vi.fn(),
  mockPatchMarkerPointerSnap: vi.fn(),
  mockBuildGeomanImportFeature: vi.fn(),
}))

vi.mock("../map-init", () => ({ ensureGeoman: mockEnsureGeoman }))
vi.mock("../core/state", () => ({ getCtx: () => ({ geoman: geomanMock }) }))
vi.mock("./edit-state", () => ({
  findLayerEntryByFeatureId: mockFindLayerEntry,
  setActiveEditEntry: mockSetActiveEditEntry,
  setActiveEditCoordsSnapshot: mockSetActiveEditCoordsSnapshot,
  setActiveGeomanFeatureId: mockSetActiveGeomanFeatureId,
  isEditMode: vi.fn(),
  disableEditMode: vi.fn(),
  suppressGeomanFill: vi.fn(),
  ensureGeomanDrawEdgesVisible: vi.fn(),
}))
vi.mock("../snapping/snapping", () => ({
  disableCrosshair: mockDisableCrosshair,
  disableSnapping: mockDisableSnapping,
  setSnapExclude: mockSetSnapExclude,
  setEditModeActive: mockSetEditModeActive,
}))
vi.mock("../edit/edit-import", () => ({ buildGeomanImportFeature: mockBuildGeomanImportFeature }))
vi.mock("../edit/edit-snap", () => ({ patchMarkerPointerSnap: mockPatchMarkerPointerSnap }))
vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
  debugLog: vi.fn(),
  debugWarn: vi.fn(),
}))

let geomanMock: any
let mod: typeof import("./edit-mode")

function entry(): LayerEntry {
  return {
    id: "feat_1",
    dbId: "db-1",
    type: "marker",
    data: {
      type: "houseEntrances",
      label: "H1",
      entranceTypeKey: "main_entrance",
      lat: 36.5,
      lng: 127.5,
    },
  } as unknown as LayerEntry
}

function makeGeoman() {
  return {
    features: { importGeoJson: vi.fn().mockResolvedValue({ addedFeatures: [{ id: "g1" }] }) },
    enableGlobalEditMode: vi.fn().mockResolvedValue(undefined),
  }
}

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  geomanMock = makeGeoman()
  mockBuildGeomanImportFeature.mockReturnValue(null)
  mod = await import("./edit-mode")
})

describe("enableEditMode", () => {
  it("returns early when geoman is not present", async () => {
    geomanMock = undefined
    await mod.enableEditMode()
    expect(mockEnsureGeoman).toHaveBeenCalled()
    expect(mockDisableCrosshair).not.toHaveBeenCalled()
  })

  it("enables global edit mode for an edit without a feature id", async () => {
    await mod.enableEditMode()
    expect(mockSetActiveEditCoordsSnapshot).not.toHaveBeenCalled()
    expect(geomanMock.enableGlobalEditMode).toHaveBeenCalled()
    expect(mockDisableCrosshair).toHaveBeenCalled()
    expect(mockDisableSnapping).toHaveBeenCalled()
    expect(mockSetEditModeActive).toHaveBeenCalledWith(true)
    expect(mockSetSnapExclude).toHaveBeenCalledWith(null)
    expect(mockPatchMarkerPointerSnap).toHaveBeenCalledWith(null)
  })

  it("imports the feature and records the coords snapshot when a feature is found", async () => {
    const e = entry()
    mockFindLayerEntry.mockReturnValue(e)
    mockBuildGeomanImportFeature.mockReturnValue({ type: "Feature" } as unknown as GeoJSON.Feature)

    await mod.enableEditMode("feat_1")

    expect(mockSetActiveEditEntry).toHaveBeenCalledWith(e)
    expect(mockSetActiveEditCoordsSnapshot).toHaveBeenCalledWith([{ lat: 36.5, lng: 127.5 }])
    expect(geomanMock.features.importGeoJson).toHaveBeenCalled()
    expect(mockSetActiveGeomanFeatureId).toHaveBeenCalledWith("g1")
    expect(geomanMock.enableGlobalEditMode).toHaveBeenCalled()
  })

  it("logs an error when geoman import fails", async () => {
    mockFindLayerEntry.mockReturnValue(entry())
    mockBuildGeomanImportFeature.mockReturnValue({ type: "Feature" } as unknown as GeoJSON.Feature)
    geomanMock.features.importGeoJson.mockRejectedValue(new Error("boom"))

    await mod.enableEditMode("feat_1")

    expect(mockDebugError).toHaveBeenCalled()
    expect(geomanMock.enableGlobalEditMode).toHaveBeenCalled()
  })

  it("does not call importGeoJson when no import feature is built", async () => {
    mockFindLayerEntry.mockReturnValue(entry())
    mockBuildGeomanImportFeature.mockReturnValue(null)

    await mod.enableEditMode("feat_1")

    expect(geomanMock.features.importGeoJson).not.toHaveBeenCalled()
    expect(geomanMock.enableGlobalEditMode).toHaveBeenCalled()
  })

  it("records a point snapshot for a lat/lng feature without a coordinates array", async () => {
    const e = {
      ...entry(),
      data: {
        type: "houseEntrances",
        label: "H1",
        entranceTypeKey: "main_entrance",
        lat: 36.0,
        lng: 127.0,
      },
    }
    mockFindLayerEntry.mockReturnValue(e as unknown as LayerEntry)
    mockBuildGeomanImportFeature.mockReturnValue({ type: "Feature" } as unknown as GeoJSON.Feature)

    await mod.enableEditMode("feat_1")

    expect(mockSetActiveEditCoordsSnapshot).toHaveBeenCalledWith([{ lat: 36.0, lng: 127.0 }])
  })
})
