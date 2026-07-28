import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { FeatureTypeKey, LayerEntry } from "../types/features"

const mockFeaturesStoreAdd = vi.fn()
const mockToast = vi.fn()
const mockToApiSaveShape = vi.fn()

vi.mock("../stores/featuresStore", () => ({
  useFeaturesStore: () => ({
    add: mockFeaturesStoreAdd,
  }),
}))

vi.mock("../lib/toast", () => ({
  showToast: mockToast,
}))

vi.mock("./features/feature-data", () => ({
  toApiSaveShape: mockToApiSaveShape,
}))

let resetUndoStack: () => void
let hasUndo: () => boolean
let getUndoLabel: () => string | null
let recordDelete: (entry: LayerEntry, phaseKey: FeatureTypeKey) => void
let undo: () => Promise<void>

async function loadModule() {
  const mod = await import("./undo")
  resetUndoStack = mod.resetUndoStack
  hasUndo = mod.hasUndo
  getUndoLabel = mod.getUndoLabel
  recordDelete = mod.recordDelete
  undo = mod.undo
}

function makeEntry(overrides: Partial<LayerEntry> = {}): LayerEntry {
  return {
    id: "feat-1",
    dbId: "db-1",
    type: "polygon",
    data: {
      type: "areas",
      label: "Test Area",
      decisionNumber: "",
      decisionDate: "",
      areaTypeKey: "central_urban",
    },
    ...overrides,
  }
}

beforeEach(async () => {
  setActivePinia(createPinia())
  mockFeaturesStoreAdd.mockReset()
  mockToast.mockReset()
  mockToApiSaveShape.mockReset()
  mockToApiSaveShape.mockReturnValue({ type: "areas", layer: "areas" })
  await loadModule()
  resetUndoStack()
})

describe("undo", () => {
  describe("resetUndoStack", () => {
    it("clears the stack", () => {
      recordDelete(makeEntry(), "areas")
      expect(hasUndo()).toBe(true)
      resetUndoStack()
      expect(hasUndo()).toBe(false)
    })
  })

  describe("hasUndo", () => {
    it("returns false when stack is empty", () => {
      expect(hasUndo()).toBe(false)
    })

    it("returns true after recording a delete", () => {
      recordDelete(makeEntry(), "areas")
      expect(hasUndo()).toBe(true)
    })
  })

  describe("getUndoLabel", () => {
    it("returns null when stack is empty", () => {
      expect(getUndoLabel()).toBeNull()
    })

    it("returns label of the last deleted feature", () => {
      recordDelete(
        makeEntry({
          data: {
            type: "areas",
            label: "My Area",
            decisionNumber: "",
            decisionDate: "",
            areaTypeKey: "central_urban",
          },
        }),
        "areas",
      )
      expect(getUndoLabel()).toBe('Restore "My Area"')
    })
  })

  describe("recordDelete", () => {
    it("pushes entry and phase key onto the stack", () => {
      const entry = makeEntry()
      recordDelete(entry, "roads")
      expect(hasUndo()).toBe(true)
      expect(getUndoLabel()).toContain(entry.data.label)
    })
  })

  describe("undo", () => {
    it("shows info toast when stack is empty", async () => {
      await undo()
      expect(mockToast).toHaveBeenCalledWith("map_nothing_to_restore", "info")
    })

    it("restores a deleted polygon feature", async () => {
      const entry = makeEntry({
        data: {
          type: "areas",
          label: "Restored Area",
          decisionNumber: "",
          decisionDate: "",
          areaTypeKey: "central_urban",
          coordinates: [
            { lat: 1, lng: 2 },
            { lat: 3, lng: 4 },
          ],
        },
      })
      recordDelete(entry, "areas")

      const mockFetch = vi.mocked(await import("../api")).apiFetch as ReturnType<typeof vi.fn>
      mockFetch.mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue({ id: "new-42" }) })

      await undo()

      expect(mockToast).toHaveBeenCalledWith("map_restored", "success")
    })

    it("shows error toast on API failure", async () => {
      recordDelete(makeEntry(), "areas")

      const mockFetch = vi.mocked(await import("../api")).apiFetch as ReturnType<typeof vi.fn>
      mockFetch.mockRejectedValue(new Error("Network failure"))

      await undo()

      expect(mockToast).toHaveBeenCalledWith("map_restore_failed", "error")
    })

    it("repairs cross-references for houseEntrances", async () => {
      const { useLayerStore } = await import("../stores/layerStore")
      const layerStore = useLayerStore()

      const oldDbId = "db-old-main"
      const entry = makeEntry({
        dbId: oldDbId,
        data: {
          type: "houseEntrances",
          label: "Main Entrance",
          entranceTypeKey: "main_entrance",
        },
      })

      const secondaryEntry = makeEntry({
        id: "sec-1",
        dbId: "db-sec-1",
        data: {
          type: "houseEntrances",
          label: "Secondary",
          entranceTypeKey: "secondary_entrance",
          mainEntranceDbId: oldDbId,
          mainEntranceLabel: "Main Entrance",
        },
      })

      layerStore.addFeature("houseEntrances", secondaryEntry)
      recordDelete(entry, "houseEntrances")

      const mockFetch = vi.mocked(await import("../api")).apiFetch as ReturnType<typeof vi.fn>
      mockFetch.mockResolvedValue({
        ok: true,
        json: vi.fn().mockResolvedValue({ id: "new-main-id" }),
      })

      await undo()

      expect(layerStore.houseEntrances[0].data.mainEntranceDbId).toBe("new-main-id")
      expect(layerStore.houseEntrances[0].data.mainEntranceLabel).toBe("Main Entrance")
    })
  })

  describe("entryDataToGeometry (via undo)", () => {
    it("handles marker features", async () => {
      recordDelete(
        makeEntry({
          type: "marker",
          data: {
            type: "namingPanels",
            label: "Marker",
            lat: 5,
            lng: 10,
          },
        }),
        "areas",
      )

      const mockFetch = vi.mocked(await import("../api")).apiFetch as ReturnType<typeof vi.fn>
      mockFetch.mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue({ id: "new-id" }) })

      await undo()

      expect(mockFeaturesStoreAdd).toHaveBeenCalled()
    })
  })
})
