import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import type { LayerEntry } from "../../types"

const {
  mockGetActiveEditEntry,
  mockGetActiveEditCoordsSnapshot,
  mockGetActiveGeomanFeatureId,
  mockSetActiveGeomanFeatureId,
  mockDisableEditMode,
  mockShowToast,
  mockBuildDrawControl,
  mockRepatchMarker,
} = vi.hoisted(() => ({
  mockGetActiveEditEntry: vi.fn(),
  mockGetActiveEditCoordsSnapshot: vi.fn(),
  mockGetActiveGeomanFeatureId: vi.fn((): string | null => null),
  mockSetActiveGeomanFeatureId: vi.fn(),
  mockDisableEditMode: vi.fn(),
  mockShowToast: vi.fn(),
  mockBuildDrawControl: vi.fn(),
  mockRepatchMarker: vi.fn(),
}))

vi.mock("./edit-state", () => ({
  getActiveEditEntry: mockGetActiveEditEntry,
  getActiveEditCoordsSnapshot: mockGetActiveEditCoordsSnapshot,
  getActiveGeomanFeatureId: mockGetActiveGeomanFeatureId,
  setActiveGeomanFeatureId: mockSetActiveGeomanFeatureId,
  disableEditMode: mockDisableEditMode,
}))
vi.mock("../core/state", () => ({ getCtx: vi.fn(() => ({ geoman: {} as any })) }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast }))
vi.mock("../../i18n", () => ({ t: (key: string) => key }))
vi.mock("../draw/draw-control", () => ({ buildDrawControl: mockBuildDrawControl }))
vi.mock("../draw/draw-complete", () => ({ repatchMarker: mockRepatchMarker }))

let useFeaturesStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())

  const fs = await import("../../stores/featuresStore")
  useFeaturesStore = fs.useFeaturesStore
})

async function loadMod() {
  return await import("./edit-commit")
}

function markerEntry(overrides: Partial<LayerEntry> = {}): LayerEntry {
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
    ...overrides,
  } as LayerEntry
}

describe("cancelEditMode", () => {
  it("restores lat/lng for marker features", async () => {
    const entry = markerEntry()
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveEditCoordsSnapshot.mockReturnValue([{ lat: 36.0, lng: 127.0 }])
    const featuresStoreUpdate = vi.spyOn(useFeaturesStore(), "update")
    const mod = await loadMod()

    await mod.cancelEditMode()

    const d = entry.data as { lat: number; lng: number; coordinates?: unknown }
    expect(d.lat).toBe(36.0)
    expect(d.lng).toBe(127.0)
    expect("coordinates" in d).toBe(false)
    expect(featuresStoreUpdate).toHaveBeenCalledWith("feat_1", {
      geometry: { type: "Point", coordinates: [127.0, 36.0] },
    })
  })

  it("restores coordinates for line/polygon features", async () => {
    const entry = {
      id: "feat_2",
      dbId: "db-2",
      type: "line",
      data: {
        type: "roads",
        label: "R1",
        decisionNumber: "",
        decisionDate: "",
        roadTypeKey: "main_road",
        coordinates: [
          { lat: 36.9, lng: 127.9 },
          { lat: 37.0, lng: 128.0 },
        ],
      },
    } as unknown as LayerEntry
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveEditCoordsSnapshot.mockReturnValue([
      { lat: 36.0, lng: 127.0 },
      { lat: 36.1, lng: 127.1 },
    ])
    const featuresStoreUpdate = vi.spyOn(useFeaturesStore(), "update")
    const mod = await loadMod()

    await mod.cancelEditMode()

    expect(entry.data.coordinates).toEqual([
      { lat: 36.0, lng: 127.0 },
      { lat: 36.1, lng: 127.1 },
    ])
    expect(featuresStoreUpdate).toHaveBeenCalledWith("feat_2", {
      geometry: {
        type: "LineString",
        coordinates: [
          [127.0, 36.0],
          [127.1, 36.1],
        ],
      },
    })
  })

  it("does nothing to data when no snapshot exists", async () => {
    const entry = markerEntry()
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveEditCoordsSnapshot.mockReturnValue(null)
    const featuresStoreUpdate = vi.spyOn(useFeaturesStore(), "update")
    const mod = await loadMod()

    await mod.cancelEditMode()

    expect(featuresStoreUpdate).not.toHaveBeenCalled()
    expect((entry.data as { lat: number }).lat).toBe(36.5)
  })
})
