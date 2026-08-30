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
  mockApiFetch,
  mockGetCtx,
  getSetCtx,
} = vi.hoisted(() => {
  let current: any = null
  return {
    mockGetActiveEditEntry: vi.fn(),
    mockGetActiveEditCoordsSnapshot: vi.fn(),
    mockGetActiveGeomanFeatureId: vi.fn((): string | null => null),
    mockSetActiveGeomanFeatureId: vi.fn(),
    mockDisableEditMode: vi.fn(),
    mockShowToast: vi.fn(),
    mockBuildDrawControl: vi.fn(),
    mockRepatchMarker: vi.fn(),
    mockApiFetch: vi.fn(async () => ({ ok: true }) as Response),
    mockGetCtx: vi.fn(() => current),
    getSetCtx: (c: any) => {
      current = c
    },
  }
})

vi.mock("./edit-state", () => ({
  getActiveEditEntry: mockGetActiveEditEntry,
  getActiveEditCoordsSnapshot: mockGetActiveEditCoordsSnapshot,
  getActiveGeomanFeatureId: mockGetActiveGeomanFeatureId,
  setActiveGeomanFeatureId: mockSetActiveGeomanFeatureId,
  disableEditMode: mockDisableEditMode,
}))
vi.mock("../core/state", () => ({ getCtx: mockGetCtx }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast }))
vi.mock("../../i18n", () => ({ t: (key: string) => key }))
vi.mock("../draw/draw-control", () => ({ buildDrawControl: mockBuildDrawControl }))
vi.mock("../draw/draw-complete", () => ({ repatchMarker: mockRepatchMarker }))
vi.mock("../../api", () => ({ apiFetch: mockApiFetch }))

let useFeaturesStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())
  getSetCtx({ geoman: null })

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
    const { useLayerStore } = await import("../../stores/layerStore")
    const entry = markerEntry()
    useLayerStore().addFeature("houseEntrances", entry)
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
    const { useLayerStore } = await import("../../stores/layerStore")
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
    useLayerStore().addFeature("roads", entry as never)
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

  it("just disables edit mode when there is no active entry", async () => {
    mockGetActiveEditEntry.mockReturnValue(null)
    const mod = await loadMod()
    await mod.cancelEditMode()
    expect(mockDisableEditMode).toHaveBeenCalled()
    expect(mockShowToast).not.toHaveBeenCalled()
  })

  it("does not re-arm draw control after cancel when no phase matches", async () => {
    const entry = {
      id: "feat_8",
      dbId: "db-8",
      type: "line",
      data: { type: "nonExistentPhase", label: "X", coordinates: [] },
    } as unknown as LayerEntry
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveEditCoordsSnapshot.mockReturnValue(null)
    const mod = await loadMod()
    await mod.cancelEditMode()
    expect(mockBuildDrawControl).not.toHaveBeenCalled()
  })
})

describe("commitEditMode", () => {
  function lineEntry(): LayerEntry {
    return {
      id: "feat_3",
      dbId: "db-3",
      type: "line",
      data: {
        type: "roads",
        label: "R2",
        decisionNumber: "",
        decisionDate: "",
        roadTypeKey: "main_road",
        coordinates: [
          { lat: 36.9, lng: 127.9 },
          { lat: 37.0, lng: 128.0 },
        ],
      },
    } as unknown as LayerEntry
  }

  function geomanWithFeatures(geo: { id?: string; type: string; coordinates?: unknown }) {
    return {
      features: {
        getAll: vi.fn(async () => ({ features: [{ id: "gm-1", geometry: geo }] })),
        delete: vi.fn(async () => {}),
      } as any,
    }
  }

  it("guards against re-entrant commits", async () => {
    const entry = lineEntry()
    mockGetActiveEditEntry.mockReturnValue(entry)
    const mod = await loadMod()
    const p1 = mod.commitEditMode()
    const p2 = mod.commitEditMode()
    await Promise.all([p1, p2])
    expect(mockApiFetch).toHaveBeenCalledTimes(1)
  })

  it("disables edit mode when there is no active entry", async () => {
    mockGetActiveEditEntry.mockReturnValue(null)
    const mod = await loadMod()
    await mod.commitEditMode()
    expect(mockDisableEditMode).toHaveBeenCalled()
  })

  it("reads geoman geometry and saves the feature on the happy path", async () => {
    const { useLayerStore } = await import("../../stores/layerStore")
    const entry = lineEntry()
    useLayerStore().addFeature("roads" as never, entry as never)
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveGeomanFeatureId.mockReturnValue("gm-1")
    const gm = geomanWithFeatures({
      id: "gm-1",
      type: "LineString",
      coordinates: [
        [127.5, 36.5],
        [127.6, 36.6],
      ],
    })
    getSetCtx({ geoman: gm })

    const featuresStoreUpdate = vi.spyOn(useFeaturesStore(), "update")
    const mod = await loadMod()

    await mod.commitEditMode()

    expect(mockApiFetch).toHaveBeenCalledWith("/api/features/db-3", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ data: entry.data }),
    })
    expect(featuresStoreUpdate).toHaveBeenCalled()
    expect(mockSetActiveGeomanFeatureId).toHaveBeenCalledWith(null)
    expect(mockDisableEditMode).toHaveBeenCalled()
    expect(mockBuildDrawControl).toHaveBeenCalled()
    expect(mockRepatchMarker).toHaveBeenCalled()
  })

  it("does not read geoman geometry when there is no active geoman feature id", async () => {
    const { useLayerStore } = await import("../../stores/layerStore")
    const entry = lineEntry()
    useLayerStore().addFeature("roads" as never, entry as never)
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveGeomanFeatureId.mockReturnValue(null)
    getSetCtx({ geoman: null })

    const mod = await loadMod()
    await mod.commitEditMode()

    expect(mockApiFetch).toHaveBeenCalledTimes(1)
  })

  it("does not disable/handle when geometry extraction returns false", async () => {
    const { useLayerStore } = await import("../../stores/layerStore")
    const entry = markerEntry()
    useLayerStore().addFeature("houseEntrances" as never, entry as never)
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveGeomanFeatureId.mockReturnValue("gm-1")
    getSetCtx({
      geoman: geomanWithFeatures({ id: "gm-1", type: "LineString", coordinates: [[1, 2]] }),
    })

    const mod = await loadMod()
    await mod.commitEditMode()

    expect(mockShowToast).toHaveBeenCalledWith("map_road_min_points", "error")
    expect(mockDisableEditMode).toHaveBeenCalled() // via cancelEditMode()
    expect(mockApiFetch).not.toHaveBeenCalled()
  })

  it("does not re-arm draw control when the save fails", async () => {
    const entry = lineEntry()
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockApiFetch.mockRejectedValueOnce(new Error("network"))
    const mod = await loadMod()
    await mod.commitEditMode()
    expect(mockShowToast).toHaveBeenCalledWith("map_geometry_save_failed", "error")
    expect(mockBuildDrawControl).not.toHaveBeenCalled()
    expect(mockRepatchMarker).not.toHaveBeenCalled()
  })

  it("logs and aborts when reading geoman geometry throws", async () => {
    const entry = lineEntry()
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveGeomanFeatureId.mockReturnValue("gm-1")
    getSetCtx({
      geoman: {
        features: {
          getAll: vi.fn(async () => {
            throw new Error("geoman boom")
          }),
        } as any,
      },
    })
    const mod = await loadMod()
    await mod.commitEditMode()
    expect(mockShowToast).toHaveBeenCalledWith("map_geometry_save_failed", "error")
    expect(mockApiFetch).not.toHaveBeenCalled()
  })

  it("extracts polygon coordinates when editing a polygon", async () => {
    const { useLayerStore } = await import("../../stores/layerStore")
    const polygonEntry = {
      id: "feat_4",
      dbId: "db-4",
      type: "polygon",
      data: { type: "areas", label: "A1", coordinates: [] },
    } as unknown as LayerEntry
    useLayerStore().addFeature("areas" as never, polygonEntry as never)
    mockGetActiveEditEntry.mockReturnValue(polygonEntry)
    mockGetActiveGeomanFeatureId.mockReturnValue("gm-1")
    getSetCtx({
      geoman: geomanWithFeatures({
        id: "gm-1",
        type: "Polygon",
        coordinates: [
          [
            [127, 36],
            [128, 36],
            [128, 37],
            [127, 37],
            [127, 36],
          ],
        ],
      }),
    })
    const mod = await loadMod()
    await mod.commitEditMode()
    expect(mockApiFetch).toHaveBeenCalledTimes(1)
  })

  it("rejects a polygon with too few points", async () => {
    const polygonEntry = {
      id: "feat_5",
      dbId: "db-5",
      type: "polygon",
      data: { type: "areas", label: "A2", coordinates: [] },
    } as unknown as LayerEntry
    mockGetActiveEditEntry.mockReturnValue(polygonEntry)
    mockGetActiveGeomanFeatureId.mockReturnValue("gm-1")
    getSetCtx({
      geoman: geomanWithFeatures({
        id: "gm-1",
        type: "Polygon",
        coordinates: [
          [
            [127, 36],
            [128, 36],
          ],
        ],
      }),
    })
    const mod = await loadMod()
    await mod.commitEditMode()
    expect(mockShowToast).toHaveBeenCalledWith("map_area_min_points", "error")
    expect(mockApiFetch).not.toHaveBeenCalled()
  })

  it("extracts point coordinates for a marker", async () => {
    const { useLayerStore } = await import("../../stores/layerStore")
    const entry = markerEntry()
    useLayerStore().addFeature("houseEntrances" as never, entry as never)
    mockGetActiveEditEntry.mockReturnValue(entry)
    mockGetActiveGeomanFeatureId.mockReturnValue("gm-1")
    getSetCtx({
      geoman: geomanWithFeatures({ id: "gm-1", type: "Point", coordinates: [128.5, 37.5] }),
    })
    const mod = await loadMod()
    await mod.commitEditMode()
    const d = entry.data as { lat: number; lng: number }
    expect(d.lat).toBe(37.5)
    expect(d.lng).toBe(128.5)
  })

  it("computes a circle radius when the entry type is circle", async () => {
    const { useLayerStore } = await import("../../stores/layerStore")
    const circleEntry = {
      id: "feat_6",
      dbId: "db-6",
      type: "circle",
      data: {
        type: "cityCenter",
        label: "C1",
        lat: 0,
        lng: 0,
        radius: 0,
        coordinates: [],
      },
    } as unknown as LayerEntry
    useLayerStore().addFeature("cityCenter" as never, circleEntry as never)
    mockGetActiveEditEntry.mockReturnValue(circleEntry)
    mockGetActiveGeomanFeatureId.mockReturnValue("gm-1")
    getSetCtx({
      geoman: geomanWithFeatures({
        id: "gm-1",
        type: "Polygon",
        coordinates: [
          [
            [0, 0],
            [1, 0],
            [1, 1],
            [0, 1],
            [0, 0],
          ],
        ],
      }),
    })
    const mod = await loadMod()
    await mod.commitEditMode()
    const d = circleEntry.data as { lat: number; lng: number; radius: number }
    expect(d.lat).toBeCloseTo(0.4)
    expect(d.lng).toBeCloseTo(0.4)
    expect(d.radius).toBeGreaterThan(0)
  })

  it("does not re-arm draw control when no phase matches the entry type", async () => {
    const entry = {
      id: "feat_7",
      dbId: "db-7",
      type: "line",
      data: { type: "nonExistentPhase", label: "X", coordinates: [] },
    } as unknown as LayerEntry
    mockGetActiveEditEntry.mockReturnValue(entry)
    const mod = await loadMod()
    await mod.commitEditMode()
    expect(mockDisableEditMode).toHaveBeenCalled()
    expect(mockBuildDrawControl).not.toHaveBeenCalled()
  })
})
