import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { LayerEntry } from "../types"

const mockApiFetch = vi.fn()
const mockFeaturesStoreUpdate = vi.fn()
const mockFeaturesStoreBatchUpdate = vi.fn()
const mockShowToast = vi.fn()
const mockDebugError = vi.fn()

vi.mock("../api", () => ({ apiFetch: mockApiFetch }))
vi.mock("../stores/featuresStore", () => ({
  useFeaturesStore: () => ({
    update: mockFeaturesStoreUpdate,
    batchUpdate: mockFeaturesStoreBatchUpdate,
  }),
}))
vi.mock("../lib/toast", () => ({ showToast: mockShowToast }))
vi.mock("../i18n", () => ({ t: (key: string) => key }))
vi.mock("../utils/debug", () => ({
  debugError: mockDebugError,
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))

let useAppStore: any
let useLayerStore: any
let setHouseNumbers: () => Promise<void>

function entrance(id: string, dbId: string, side: "left" | "right"): LayerEntry {
  return {
    id,
    dbId,
    type: "marker",
    data: {
      type: "houseEntrances",
      label: "?",
      entranceTypeKey: "main_entrance",
      roadDbId: "road-1",
      side,
      lat: 36.0,
      lng: 127.0,
    },
  } as LayerEntry
}

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  mockApiFetch.mockResolvedValue({
    ok: true,
    json: async () => ({ success: true, entrances: [] }),
  })

  const as = await import("../stores/appStore")
  useAppStore = as.useAppStore
  const ls = await import("../stores/layerStore")
  useLayerStore = ls.useLayerStore
  const mod = await import("./house-numbering")
  setHouseNumbers = mod.setHouseNumbers
})

async function seedRoad(): Promise<void> {
  const store = useLayerStore()
  store.addFeature("roads", {
    id: "road-1",
    dbId: "road-1",
    type: "line",
    data: {
      type: "roads",
      label: "R1",
      decisionNumber: "",
      decisionDate: "",
      roadTypeKey: "street",
      coordinates: [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.001, lng: 127.001 },
      ],
    },
  })
}

describe("setHouseNumbers", () => {
  it("shows error when no reference road is selected", async () => {
    await setHouseNumbers()
    expect(mockShowToast).toHaveBeenCalledWith("alert_no_ref_road", "error")
  })

  it("shows info toast when no unassigned entrances exist", async () => {
    useAppStore().referenceRoadDbId = "road-1"
    await seedRoad()
    await setHouseNumbers()
    expect(mockShowToast).toHaveBeenCalledWith("alert_no_unassigned_entrances", "info")
  })

  it("sends the ordered batch and applies the authoritative numbers returned by the server", async () => {
    useAppStore().referenceRoadDbId = "road-1"
    await seedRoad()
    const store = useLayerStore()
    const right = entrance("e2", "e-db-2", "right")
    const left = entrance("e1", "e-db-1", "left")
    store.addFeature("houseEntrances", left)
    store.addFeature("houseEntrances", right)

    // Note: e1 is placed closer to the road start (lng 127.0) than e2
    // (lng 127.001), so the batch order is [left, right] regardless of the
    // order they were added to the store.
    mockApiFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        success: true,
        entrances: [
          { id: "e-db-1", side: "left", entranceNumber: 1, label: "1" },
          { id: "e-db-2", side: "right", entranceNumber: 2, label: "2" },
        ],
      }),
    })

    await setHouseNumbers()

    expect(mockApiFetch).toHaveBeenCalledTimes(1)
    const [url, init] = mockApiFetch.mock.calls[0]
    expect(url).toBe("/api/features/number-entrances")
    expect(init.method).toBe("POST")
    expect(JSON.parse(init.body)).toEqual({
      roadId: "road-1",
      entranceIds: ["e-db-1", "e-db-2"],
    })
    // Applied numbers come from the server response, not computed client-side.
    expect((left.data as { entranceNumber: number }).entranceNumber).toBe(1)
    expect((left.data as { label: string }).label).toBe("1")
    expect((right.data as { entranceNumber: number }).entranceNumber).toBe(2)
    expect(mockFeaturesStoreBatchUpdate).toHaveBeenCalledWith(
      expect.arrayContaining([
        { id: "e1", properties: expect.objectContaining({ label: "1" }) },
        { id: "e2", properties: expect.objectContaining({ label: "2" }) },
      ]),
    )
    expect(mockShowToast).toHaveBeenCalledWith("map_assigned_numbers", "success")
  })

  it("does not mutate entrances and shows an error toast when the batch request fails", async () => {
    useAppStore().referenceRoadDbId = "road-1"
    await seedRoad()
    const store = useLayerStore()
    const left = entrance("e1", "e-db-1", "left")
    const right = entrance("e2", "e-db-2", "right")
    store.addFeature("houseEntrances", left)
    store.addFeature("houseEntrances", right)

    mockApiFetch.mockRejectedValue(new Error("Network failure"))

    await setHouseNumbers()

    expect(mockApiFetch).toHaveBeenCalledTimes(1)
    expect((left.data as { entranceNumber: number }).entranceNumber).toBeUndefined()
    expect((right.data as { entranceNumber: number }).entranceNumber).toBeUndefined()
    expect((right.data as { label: string }).label).toBe("?")
    expect(mockFeaturesStoreBatchUpdate).not.toHaveBeenCalled()
    expect(mockShowToast).toHaveBeenCalledWith("map_assigned_numbers_error", "error")
  })
})
