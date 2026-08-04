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

    it("ignores a second undo while one is in flight", async () => {
      recordDelete(makeEntry(), "areas")
      recordDelete(makeEntry({ id: "feat-2", dbId: "db-2" }), "areas")

      let releaseFirst: () => void
      const firstGate = new Promise<void>((resolve) => {
        releaseFirst = resolve
      })
      const mockFetch = vi.mocked(await import("../api")).apiFetch as ReturnType<typeof vi.fn>
      mockFetch.mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            firstGate.then(() =>
              resolve({ ok: true, json: vi.fn().mockResolvedValue({ id: "new-1" }) }),
            )
          }),
      )
      mockFetch.mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue({ id: "new-2" }) })

      const first = undo()
      const second = undo()
      releaseFirst!()
      await Promise.all([first, second])

      // Second call was skipped while the first was in flight — only one restore.
      expect(mockFeaturesStoreAdd).toHaveBeenCalledTimes(1)
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

      const putCall = mockFetch.mock.calls.find(
        (c) => c[0] === "/api/features/db-sec-1" && c[1]?.method === "PUT",
      )
      expect(putCall).toBeTruthy()
      const putBody = JSON.parse(putCall![1]!.body as string)
      expect(putBody.data).toMatchObject({ mainEntranceDbId: "new-main-id" })
    })

    it("repairs roadDbId cross-references after restoring a road", async () => {
      const { useLayerStore } = await import("../stores/layerStore")
      const layerStore = useLayerStore()

      const oldDbId = "db-old-road"
      const roadEntry = makeEntry({
        dbId: oldDbId,
        data: {
          type: "roads",
          label: "Restored Road",
          decisionNumber: "",
          decisionDate: "",
          roadTypeKey: "main_road",
        },
      })

      const entranceEntry = makeEntry({
        id: "ent-1",
        dbId: "db-ent-1",
        type: "marker",
        data: {
          type: "houseEntrances",
          label: "Entrance on road",
          entranceTypeKey: "secondary_entrance",
          roadDbId: oldDbId,
        },
      })

      layerStore.addFeature("houseEntrances", entranceEntry)
      recordDelete(roadEntry, "roads")

      const mockFetch = vi.mocked(await import("../api")).apiFetch as ReturnType<typeof vi.fn>
      mockFetch.mockResolvedValue({
        ok: true,
        json: vi.fn().mockResolvedValue({ id: "new-road-id" }),
      })

      await undo()

      expect(layerStore.houseEntrances[0].data.roadDbId).toBe("new-road-id")
      const putCall = mockFetch.mock.calls.find(
        (c) => c[0] === "/api/features/db-ent-1" && c[1]?.method === "PUT",
      )
      expect(putCall).toBeTruthy()
      const putBody = JSON.parse(putCall![1]!.body as string)
      expect(putBody.data).toMatchObject({ roadDbId: "new-road-id" })
    })

    it("shows warning toast when a cross-reference repair fails to persist", async () => {
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

      layerStore.addFeature(
        "houseEntrances",
        makeEntry({
          id: "sec-2",
          dbId: "db-sec-2",
          data: {
            type: "houseEntrances",
            label: "Secondary",
            entranceTypeKey: "secondary_entrance",
            mainEntranceDbId: oldDbId,
            mainEntranceLabel: "Main Entrance",
          },
        }),
      )
      recordDelete(entry, "houseEntrances")

      const mockFetch = vi.mocked(await import("../api")).apiFetch as ReturnType<typeof vi.fn>
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: vi.fn().mockResolvedValue({ id: "new-main-id" }),
      })
      mockFetch.mockRejectedValueOnce(new Error("Network failure"))

      await undo()

      expect(mockToast).toHaveBeenCalledWith("map_restored", "success")
      expect(mockToast).toHaveBeenCalledWith("map_restore_refs_warning", "warning")
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
