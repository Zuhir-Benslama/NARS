import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import type { LayerEntry } from "../types"

const mockBatchAdd = vi.hoisted(() => vi.fn())
const mockApiFetch = vi.hoisted(() => vi.fn())

vi.mock("../stores/featuresStore", () => ({
  useFeaturesStore: () => ({ batchAdd: mockBatchAdd }),
}))

vi.mock("../api", () => ({ apiFetch: mockApiFetch }))

let useLayerStore: any
let generateNamingPanels: () => Promise<void>

function district(label: string, coords: { lat: number; lng: number }[]): LayerEntry {
  return {
    id: `d-${label}`,
    dbId: `d-${label}`,
    type: "polygon",
    data: { type: "districts", label, coordinates: coords },
  } as LayerEntry
}

function road(label: string, coords: { lat: number; lng: number }[]): LayerEntry {
  return {
    id: `r-${label}`,
    dbId: `r-${label}`,
    type: "line",
    data: { type: "roads", label, coordinates: coords },
  } as LayerEntry
}

beforeEach(async () => {
  vi.clearAllMocks()
  mockApiFetch.mockResolvedValue({
    ok: true,
    json: vi.fn().mockResolvedValue({ id: "db-panel-1" }),
  })
  setActivePinia(createPinia())
  const ls = await import("../stores/layerStore")
  useLayerStore = ls.useLayerStore
  const mod = await import("./naming-panels")
  generateNamingPanels = mod.generateNamingPanels
})

describe("generateNamingPanels", () => {
  it("persists generated panels and uses real dbIds", async () => {
    const store = useLayerStore()
    store.addFeature(
      "districts",
      district("D1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.001, lng: 127.001 },
        { lat: 36.001, lng: 127.0 },
      ]),
    )

    await generateNamingPanels()

    expect(mockApiFetch).toHaveBeenCalledTimes(3)
    const body = JSON.parse((mockApiFetch.mock.calls[0][1] as { body: string }).body)
    expect(body).toMatchObject({ type: "naming_panel", layer: "naming_panel", label: "D1" })
    const panel = store.$state.namingPanels[0]
    expect(panel.dbId).toBe("db-panel-1")
  })

  it("excludes panels from store when persistence fails", async () => {
    mockApiFetch.mockResolvedValue({ ok: false, status: 500, json: vi.fn().mockResolvedValue({}) })
    const store = useLayerStore()
    store.addFeature(
      "districts",
      district("D1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.001, lng: 127.001 },
        { lat: 36.001, lng: 127.0 },
      ]),
    )

    await generateNamingPanels()

    expect(mockBatchAdd).not.toHaveBeenCalled()
    expect(store.$state.namingPanels).toHaveLength(0)
  })

  it("batches all generated panels into a single setData", async () => {
    const store = useLayerStore()
    store.addFeature(
      "districts",
      district("D1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.001, lng: 127.001 },
        { lat: 36.001, lng: 127.0 },
      ]),
    )
    store.addFeature(
      "roads",
      road("R1", [
        { lat: 36.002, lng: 127.0 },
        { lat: 36.0025, lng: 127.0 },
      ]),
    )

    await generateNamingPanels()

    expect(mockBatchAdd).toHaveBeenCalledTimes(1)
    const added = mockBatchAdd.mock.calls[0][0] as {
      properties: { phaseKey: string; label: string }
    }[]
    const panelLabels = added.map((f) => f.properties.label).sort()
    expect(panelLabels).toEqual(["D1", "D1", "D1", "R1", "R1"])
    expect(store.$state.namingPanels.length).toBe(5)
  })

  it("dedupes panels within the threshold distance", async () => {
    const store = useLayerStore()
    store.addFeature("publicBuildings", {
      id: "pb-1",
      dbId: "pb-1",
      type: "polygon",
      data: {
        type: "publicBuildings",
        label: "PB1",
        coordinates: [
          { lat: 36.0, lng: 127.0 },
          { lat: 36.0001, lng: 127.0001 },
          { lat: 36.0, lng: 127.0 },
        ],
      },
    } as LayerEntry)
    store.addFeature("publicSpaces", {
      id: "ps-1",
      dbId: "ps-1",
      type: "polygon",
      data: {
        type: "publicSpaces",
        label: "PS1",
        coordinates: [
          { lat: 36.0, lng: 127.0 },
          { lat: 36.00005, lng: 127.00005 },
          { lat: 36.0, lng: 127.0 },
        ],
      },
    } as LayerEntry)

    await generateNamingPanels()

    const added = mockBatchAdd.mock.calls[0][0] as { properties: { label: string } }[]
    expect(added).toHaveLength(1)
    expect(store.$state.namingPanels.length).toBe(1)
  })

  it("does not call batchAdd when there is nothing to place", async () => {
    await generateNamingPanels()
    expect(mockBatchAdd).not.toHaveBeenCalled()
    expect(mockApiFetch).not.toHaveBeenCalled()
  })
})
