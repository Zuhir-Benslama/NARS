import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

const {
  mockApiFetch,
  mockRenderScatteredAreas,
  mockRefreshLayerVisibility,
  mockGetFeatureType,
  mockDebugError,
  mockDebugLog,
  mockUpdateEndpointMarkers,
  mockLoadPhase,
  mockBuildGeoJsonFeature,
  mockFeaturesStoreBatchAdd,
  mockFeaturesStoreClear,
} = vi.hoisted(() => ({
  mockApiFetch: vi.fn(),
  mockRenderScatteredAreas: vi.fn(),
  mockRefreshLayerVisibility: vi.fn(),
  mockGetFeatureType: vi.fn(),
  mockDebugError: vi.fn(),
  mockDebugLog: vi.fn(),
  mockUpdateEndpointMarkers: vi.fn(),
  mockLoadPhase: vi.fn(),
  mockBuildGeoJsonFeature: vi.fn(),
  mockFeaturesStoreBatchAdd: vi.fn(),
  mockFeaturesStoreClear: vi.fn(),
}))

vi.mock("../../api", () => ({ apiFetch: mockApiFetch }))
vi.mock("../rendering/geometry", () => ({ renderScatteredAreas: mockRenderScatteredAreas }))
vi.mock("../rendering/labels", () => ({ refreshLayerVisibility: mockRefreshLayerVisibility }))
vi.mock("../house-numbering", () => ({ getFeatureType: mockGetFeatureType }))
vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
  debugLog: mockDebugLog,
  debugWarn: vi.fn(),
}))
vi.mock("../roads/road-directions", () => ({ updateEndpointMarkers: mockUpdateEndpointMarkers }))
vi.mock("../../phases-nav/storage", () => ({ loadPhase: mockLoadPhase }))
vi.mock("./loader-build", () => ({ buildGeoJsonFeature: mockBuildGeoJsonFeature }))

import { _setCtx } from "../core/state"

vi.mock("../../stores/featuresStore", () => ({
  useFeaturesStore: () => ({
    clear: mockFeaturesStoreClear,
    batchAdd: mockFeaturesStoreBatchAdd,
    updateSource: vi.fn(),
  }),
}))

let loadFromDatabase: () => Promise<void>

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  _setCtx({} as any)

  const mod = await import("./loader-db")
  loadFromDatabase = mod.loadFromDatabase
})

describe("loader-db", () => {
  it("returns early when API returns empty array", async () => {
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([]) })

    await loadFromDatabase()

    expect(mockDebugLog).toHaveBeenCalledWith("[LOAD] No saved features in database.")
  })

  it("returns early when API returns {features: []}", async () => {
    mockApiFetch.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ features: [], count: 0 }),
    })

    await loadFromDatabase()

    expect(mockDebugLog).toHaveBeenCalledWith("[LOAD] No saved features in database.")
  })

  it("processes a road feature successfully", async () => {
    const feature = {
      id: "1",
      layer: "street",
      data: { type: "roads", coordinates: [{ lat: 36.0, lng: 127.0 }], label: "Main St" },
      geometry: null,
    }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })
    mockGetFeatureType.mockReturnValue("geojson")
    mockBuildGeoJsonFeature.mockReturnValue({
      geometry: { type: "Point", coordinates: [127.0, 36.0] },
      properties: { label: "Main St" },
    })

    await loadFromDatabase()

    expect(mockApiFetch).toHaveBeenCalledWith("/api/features")
    expect(mockFeaturesStoreBatchAdd).toHaveBeenCalledTimes(1)
    expect(mockFeaturesStoreBatchAdd.mock.calls[0][0]).toHaveLength(1)
  })

  it("handles scattered features", async () => {
    const feature = {
      id: "2",
      layer: "scattered",
      data: { geometry: { type: "Point", coordinates: [0, 0] } },
    }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })

    await loadFromDatabase()

    expect(mockRenderScatteredAreas).toHaveBeenCalledWith(feature.data.geometry)
    expect(mockFeaturesStoreBatchAdd).toHaveBeenCalledWith([])
  })

  it("handles unknown layer gracefully", async () => {
    const feature = { id: "3", layer: "unknown_layer", data: { type: "nope", label: "X" } }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })

    await loadFromDatabase()

    expect(mockDebugError).toHaveBeenCalled()
    expect(mockFeaturesStoreBatchAdd).toHaveBeenCalledWith([])
  })

  it("parses feature.data when it is a string", async () => {
    const feature = {
      id: "1",
      layer: "street",
      data: JSON.stringify({
        type: "roads",
        label: "Parsed St",
        coordinates: [{ lat: 36.0, lng: 127.0 }],
      }),
    }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })
    mockGetFeatureType.mockReturnValue("geojson")
    mockBuildGeoJsonFeature.mockReturnValue({
      geometry: { type: "Point", coordinates: [127.0, 36.0] },
      properties: { label: "Parsed St" },
    })

    await loadFromDatabase()

    expect(mockFeaturesStoreBatchAdd).toHaveBeenCalledTimes(1)
    const batchFeatures = mockFeaturesStoreBatchAdd.mock.calls[0][0]
    expect(batchFeatures).toHaveLength(1)
    expect(batchFeatures[0].properties.label).toBe("Parsed St")
  })

  it("handles cityCenter feature and updates appStore", async () => {
    const feature = {
      id: "4",
      layer: "city_center",
      data: { type: "cityCenter", label: "CC", lat: 36.0, lng: 127.5 },
    }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })
    mockGetFeatureType.mockReturnValue("circle")
    mockBuildGeoJsonFeature.mockReturnValue({
      geometry: { type: "Point", coordinates: [127.5, 36.0] },
      properties: { label: "CC" },
    })

    const { useAppStore } = await import("../../stores/appStore")
    const { useLayerStore } = await import("../../stores/layerStore")

    await loadFromDatabase()

    expect(useLayerStore().cityCenter).toHaveLength(1)
    expect(useAppStore().cityCenterLatLng).toEqual({ lat: 36.0, lng: 127.5 })
  })

  it("restores persisted phase index", async () => {
    const feature = {
      id: "1",
      layer: "city_center",
      data: { type: "cityCenter", label: "X", lat: 36.0, lng: 127.0 },
    }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })
    mockGetFeatureType.mockReturnValue("circle")
    mockBuildGeoJsonFeature.mockReturnValue(null)
    mockLoadPhase.mockReturnValue(3)

    const { useAppStore } = await import("../../stores/appStore")

    await loadFromDatabase()

    expect(useAppStore().currentPhase).toBe(3)
  })

  it("falls back to phase 0 when persisted phase is invalid", async () => {
    const feature = {
      id: "1",
      layer: "city_center",
      data: { type: "cityCenter", label: "X", lat: 36.0, lng: 127.0 },
    }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })
    mockGetFeatureType.mockReturnValue("circle")
    mockBuildGeoJsonFeature.mockReturnValue(null)
    mockLoadPhase.mockReturnValue(null)

    const { useAppStore } = await import("../../stores/appStore")

    await loadFromDatabase()

    expect(useAppStore().currentPhase).toBe(0)
  })

  it("sets loading states and calls post-load hooks", async () => {
    const feature = {
      id: "1",
      layer: "city_center",
      data: { type: "cityCenter", label: "X", lat: 36.0, lng: 127.0 },
    }
    mockApiFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve([feature]) })
    mockGetFeatureType.mockReturnValue("circle")
    mockBuildGeoJsonFeature.mockReturnValue(null)
    mockLoadPhase.mockReturnValue(0)

    const { useAppStore } = await import("../../stores/appStore")
    await loadFromDatabase()

    expect(useAppStore().isLoading).toBe(false)
    expect(mockRefreshLayerVisibility).toHaveBeenCalled()
    expect(mockUpdateEndpointMarkers).toHaveBeenCalled()
  })

  it("sets loadError on API failure", async () => {
    mockApiFetch.mockRejectedValue(new Error("API down"))

    const { useAppStore } = await import("../../stores/appStore")
    await loadFromDatabase()

    expect(useAppStore().loadError).toBe(true)
    expect(useAppStore().isLoading).toBe(false)
  })
})
