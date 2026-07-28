import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { LayerEntry, LatLng } from "../../types"

const mockUnpatchMarkerPointerSnap = vi.fn()
const mockEnableCrosshair = vi.fn()
const mockDisableSnapping = vi.fn()
const mockEnableSnapping = vi.fn()
const mockSetSnapExclude = vi.fn()
const mockSetEditModeActive = vi.fn()
const mockUpdateSelectionHighlight = vi.fn()
const mockDisableGlobalEditMode = vi.fn()
const mockGetLayer = vi.fn()
const mockSetPaintProperty = vi.fn()
const mockAddLayer = vi.fn()
const mockGetSource = vi.fn()

const mockStateCtx = {
  map: {
    getLayer: mockGetLayer,
    setPaintProperty: mockSetPaintProperty,
    addLayer: mockAddLayer,
    getSource: mockGetSource,
  },
  geoman: {
    disableGlobalEditMode: mockDisableGlobalEditMode,
  },
} as any

vi.mock("../core/state", () => ({
  getCtx: () => mockStateCtx,
  updateSelectionHighlight: mockUpdateSelectionHighlight,
}))

vi.mock("./edit-snap", () => ({
  unpatchMarkerPointerSnap: mockUnpatchMarkerPointerSnap,
}))

vi.mock("../snapping/snapping", () => ({
  enableCrosshair: mockEnableCrosshair,
  disableSnapping: mockDisableSnapping,
  enableSnapping: mockEnableSnapping,
  setSnapExclude: mockSetSnapExclude,
  setEditModeActive: mockSetEditModeActive,
}))

let isEditMode: () => boolean
let getActiveGeomanFeatureId: () => string | null
let getActiveEditEntry: () => LayerEntry | null
let getActiveEditCoordsSnapshot: () => LatLng[] | null
let setActiveGeomanFeatureId: (id: string | null) => void
let setActiveEditCoordsSnapshot: (snapshot: LatLng[] | null) => void
let setActiveEditEntry: (entry: LayerEntry | null) => void
let resetEditState: () => void
let disableEditMode: () => void
let findLayerEntryByFeatureId: (featureId: string | undefined) => LayerEntry | null
let suppressGeomanFill: () => void
let ensureGeomanDrawEdgesVisible: () => void

async function loadModule() {
  const mod = await import("./edit-state")
  isEditMode = mod.isEditMode
  getActiveGeomanFeatureId = mod.getActiveGeomanFeatureId
  getActiveEditEntry = mod.getActiveEditEntry
  getActiveEditCoordsSnapshot = mod.getActiveEditCoordsSnapshot
  setActiveGeomanFeatureId = mod.setActiveGeomanFeatureId
  setActiveEditCoordsSnapshot = mod.setActiveEditCoordsSnapshot
  setActiveEditEntry = mod.setActiveEditEntry
  resetEditState = mod.resetEditState
  disableEditMode = mod.disableEditMode
  findLayerEntryByFeatureId = mod.findLayerEntryByFeatureId
  suppressGeomanFill = mod.suppressGeomanFill
  ensureGeomanDrawEdgesVisible = mod.ensureGeomanDrawEdgesVisible
}

beforeEach(async () => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  mockStateCtx.geoman = { disableGlobalEditMode: mockDisableGlobalEditMode }
  await loadModule()
})

describe("edit-state", () => {
  describe("store wrappers", () => {
    it("isEditMode returns edit store state", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      expect(isEditMode()).toBe(false)
      useEditStore().isEditMode = true
      expect(isEditMode()).toBe(true)
    })

    it("getActiveGeomanFeatureId returns feature id", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      expect(getActiveGeomanFeatureId()).toBeNull()
      useEditStore().setActiveGeomanFeatureId("gm-1")
      expect(getActiveGeomanFeatureId()).toBe("gm-1")
    })

    it("getActiveEditEntry returns active entry", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      expect(getActiveEditEntry()).toBeNull()
      const entry: LayerEntry = {
        id: "e1",
        dbId: "db-1",
        type: "polygon",
        data: {
          type: "areas",
          label: "Test",
          decisionNumber: "",
          decisionDate: "",
          areaTypeKey: "central_urban",
        },
      }
      useEditStore().setActiveEditEntry(entry)
      expect(getActiveEditEntry()).toStrictEqual(entry)
    })

    it("getActiveEditCoordsSnapshot returns snapshot", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      expect(getActiveEditCoordsSnapshot()).toBeNull()
      const coords: LatLng[] = [{ lat: 1, lng: 2 }]
      useEditStore().setActiveEditCoordsSnapshot(coords)
      expect(getActiveEditCoordsSnapshot()).toStrictEqual(coords)
    })

    it("setActiveGeomanFeatureId stores the id", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      setActiveGeomanFeatureId("gm-42")
      expect(useEditStore().activeGeomanFeatureId).toBe("gm-42")
    })

    it("setActiveEditCoordsSnapshot stores snapshot", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const coords: LatLng[] = [{ lat: 3, lng: 4 }]
      setActiveEditCoordsSnapshot(coords)
      expect(useEditStore().activeEditCoordsSnapshot).toStrictEqual(coords)
    })

    it("setActiveEditEntry stores entry", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const entry: LayerEntry = {
        id: "e2",
        dbId: "db-2",
        type: "line",
        data: {
          type: "roads",
          label: "Road",
          decisionNumber: "",
          decisionDate: "",
          roadTypeKey: "street",
        },
      }
      setActiveEditEntry(entry)
      expect(useEditStore().activeEditEntry).toStrictEqual(entry)
    })

    it("resetEditState calls $reset on edit store", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      useEditStore().isEditMode = true
      useEditStore().setActiveGeomanFeatureId("gm-1")
      resetEditState()
      expect(useEditStore().isEditMode).toBe(false)
      expect(useEditStore().activeGeomanFeatureId).toBeNull()
    })
  })

  describe("disableEditMode", () => {
    it("returns early when ctx.geoman is missing", async () => {
      mockStateCtx.geoman = undefined

      disableEditMode()
      expect(mockUnpatchMarkerPointerSnap).not.toHaveBeenCalled()
    })

    it("disables edit mode and resets all related state", async () => {
      const { useEditStore } = await import("../../stores/editStore")
      const store = useEditStore()
      store.isEditMode = true
      store.setActiveGeomanFeatureId("gm-1")
      store.setActiveEditEntry({
        id: "e1",
        dbId: "db-1",
        type: "polygon",
        data: {
          type: "areas",
          label: "X",
          decisionNumber: "",
          decisionDate: "",
          areaTypeKey: "central_urban",
        },
      })
      store.setActiveEditCoordsSnapshot([{ lat: 1, lng: 2 }])

      disableEditMode()

      expect(mockUnpatchMarkerPointerSnap).toHaveBeenCalledOnce()
      expect(mockDisableGlobalEditMode).toHaveBeenCalledOnce()
      expect(store.isEditMode).toBe(false)
      expect(mockSetEditModeActive).toHaveBeenCalledWith(false)
      expect(store.activeGeomanFeatureId).toBeNull()
      expect(store.activeEditEntry).toBeNull()
      expect(store.activeEditCoordsSnapshot).toBeNull()
      expect(mockSetSnapExclude).toHaveBeenCalledWith(null)
      expect(mockUpdateSelectionHighlight).toHaveBeenCalledWith(null)
      expect(mockEnableCrosshair).toHaveBeenCalledOnce()
      expect(mockDisableSnapping).toHaveBeenCalledOnce()
      expect(mockEnableSnapping).toHaveBeenCalledOnce()
    })
  })

  describe("findLayerEntryByFeatureId", () => {
    it("returns null for undefined featureId", () => {
      expect(findLayerEntryByFeatureId(undefined)).toBeNull()
    })

    it("finds an entry by id across all layer state keys", async () => {
      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      const entry: LayerEntry = {
        id: "feat-roads-1",
        dbId: "db-roads-1",
        type: "line",
        data: {
          type: "roads",
          label: "Main Road",
          decisionNumber: "",
          decisionDate: "",
          roadTypeKey: "street",
        },
      }
      store.addFeature("roads", entry)

      const result = findLayerEntryByFeatureId("feat-roads-1")
      expect(result).toStrictEqual(entry)
    })

    it("returns null when no entry matches", () => {
      const result = findLayerEntryByFeatureId("nonexistent")
      expect(result).toBeNull()
    })
  })

  describe("suppressGeomanFill", () => {
    it("hides polygon fill layers when they exist", () => {
      mockGetLayer.mockImplementation((id: string) => (id.includes("polygon") ? true : false))

      suppressGeomanFill()

      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_main-polygon__fill-layer-0",
        "fill-opacity",
        0,
      )
      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_temporary-polygon__fill-layer-0",
        "fill-opacity",
        0,
      )
    })

    it("hides circle layers when they exist", () => {
      mockGetLayer.mockImplementation((id: string) => (id.includes("circle") ? true : false))

      suppressGeomanFill()

      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_main-circle__circle-layer-0",
        "circle-opacity",
        0,
      )
      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_main-circle__circle-layer-0",
        "circle-stroke-opacity",
        0,
      )
      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_temporary-circle__circle-layer-0",
        "circle-opacity",
        0,
      )
      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_temporary-circle__circle-layer-0",
        "circle-stroke-opacity",
        0,
      )
    })

    it("does not throw when layers do not exist", () => {
      mockGetLayer.mockReturnValue(false)
      expect(() => suppressGeomanFill()).not.toThrow()
    })
  })

  describe("ensureGeomanDrawEdgesVisible", () => {
    beforeEach(() => {
      mockGetSource.mockReturnValue(true)
    })

    it("sets paint properties on temporary layers", () => {
      mockGetLayer.mockImplementation((id: string) =>
        id.startsWith("gm_temporary") && id.includes("line") ? true : false,
      )

      ensureGeomanDrawEdgesVisible()

      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_temporary-polygon__line-layer-0",
        "line-opacity",
        0.8,
      )
      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_temporary-line__line-layer-0",
        "line-opacity",
        0.8,
      )
    })

    it("sets paint properties on main layers", () => {
      mockGetLayer.mockImplementation((id: string) =>
        id.startsWith("gm_main") && id.includes("line") ? true : false,
      )

      ensureGeomanDrawEdgesVisible()

      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_main-polygon__line-layer-0",
        "line-opacity",
        0.8,
      )
      expect(mockSetPaintProperty).toHaveBeenCalledWith(
        "gm_main-line__line-layer-0",
        "line-opacity",
        0.8,
      )
    })

    it("adds nars-temp-edge layer when source exists but layer does not", () => {
      mockGetLayer.mockReturnValue(false)

      ensureGeomanDrawEdgesVisible()

      expect(mockAddLayer).toHaveBeenCalledOnce()
      expect(mockAddLayer.mock.calls[0][0]).toMatchObject({
        id: "nars-temp-edge",
        type: "line",
        source: "gm_temporary",
      })
    })

    it("does not add nars-temp-edge when it already exists", () => {
      mockGetLayer.mockImplementation((id: string) => id === "nars-temp-edge")

      ensureGeomanDrawEdgesVisible()

      expect(mockAddLayer).not.toHaveBeenCalled()
    })

    it("does not add nars-temp-edge when gm_temporary source is missing", () => {
      mockGetLayer.mockReturnValue(false)
      mockGetSource.mockReturnValue(false)

      ensureGeomanDrawEdgesVisible()

      expect(mockAddLayer).not.toHaveBeenCalled()
    })

    it("does not throw when getLayer throws for individual layers", () => {
      mockGetLayer.mockImplementation((id: string) => {
        if (id === "nars-temp-edge") return false
        throw new Error("layer not found")
      })

      expect(() => ensureGeomanDrawEdgesVisible()).not.toThrow()
    })
  })
})
