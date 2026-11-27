import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { LayerEntry } from "../../types"
import { ROUNDABOUTS_CONFIG } from "../../config"

// ─── HOISTED MODULE MOCKS ────────────────────────────────────────────────────
// These replace external dependencies so the orchestration flow is testable.

const mockFeaturesStoreAdd = vi.fn()
const mockDisableDraw = vi.fn()
const mockOpenModal = vi.fn()
const mockSaveToDatabase = vi.fn()
const mockRefreshLayers = vi.fn()
const mockUpdateEndpoints = vi.fn()
const mockToast = vi.fn()
const mockBuildDrawControl = vi.fn()

vi.mock("../core/state", () => ({
  getCtx: () => ({ geoman: { disableDraw: mockDisableDraw } }),
}))

vi.mock("../../stores/featuresStore", () => ({
  useFeaturesStore: () => ({ add: mockFeaturesStoreAdd }),
}))

vi.mock("./draw-modal", () => ({
  openModalForFeature: mockOpenModal,
}))

vi.mock("../features/feature-persistence", () => ({
  saveToDatabase: mockSaveToDatabase,
}))

vi.mock("../rendering/labels", () => ({
  refreshLayerVisibility: mockRefreshLayers,
}))

vi.mock("../roads/road-directions", () => ({
  updateEndpointMarkers: mockUpdateEndpoints,
}))

vi.mock("../../lib/toast", () => ({
  showToast: mockToast,
}))

vi.mock("./draw-control", () => ({
  buildDrawControl: mockBuildDrawControl,
  clearEdgeVisibilityPoll: vi.fn(),
}))

// ─── MODULE REFERENCES (loaded dynamically) ───────────────────────────────────

let completeDrawingWithGeometry: (
  geometry: GeoJSON.Geometry,
  narsDrawType: string,
  geomanFeatureData: Record<string, unknown>,
) => Promise<void>
let normalizeGeometry: (
  geometry: GeoJSON.Geometry,
  drawType: string,
) => GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon
let resetDrawState: () => void
let setDrawingPhase: (phase: (typeof import("../../phases").PHASES)[number] | null) => void

async function loadModule() {
  const mod = await import("./draw-save")
  completeDrawingWithGeometry = mod.completeDrawingWithGeometry
  normalizeGeometry = mod.normalizeGeometry

  const state = await import("./draw-state")
  resetDrawState = state.resetDrawState
  setDrawingPhase = state.setDrawingPhase
}

// ─── HELPERS ──────────────────────────────────────────────────────────────────

const areaPhase = (await import("../../phases")).PHASES[0]
const roundaboutsPhase = (await import("../../phases")).PHASES.find(
  (phase) => phase.key === "roundabouts",
)!
const houseEntrancesPhase = (await import("../../phases")).PHASES.find(
  (phase) => phase.key === "houseEntrances",
)!

function polygonGeometry(): GeoJSON.Polygon {
  return {
    type: "Polygon",
    coordinates: [
      [
        [1, 2],
        [3, 4],
        [5, 6],
        [1, 2],
      ],
    ],
  }
}

function roundaboutsPoint(lng: number, lat: number): GeoJSON.Point & { radius: number } {
  return { type: "Point", coordinates: [lng, lat], radius: 100 }
}

function urbanArea(dbId: string, lat: number, lng: number): LayerEntry {
  return {
    id: dbId,
    dbId,
    type: "polygon",
    data: {
      type: "areas",
      label: dbId,
      areaTypeKey: "central_urban",
      coordinates: [
        { lat: lat - 1, lng: lng - 1 },
        { lat: lat - 1, lng: lng + 1 },
        { lat: lat + 1, lng: lng + 1 },
        { lat: lat + 1, lng: lng - 1 },
        { lat: lat - 1, lng: lng - 1 },
      ],
    },
  } as LayerEntry
}

function roundabouts(
  dbId: string,
  lat: number,
  lng: number,
  roundaboutsTypeKey = "city_center",
): LayerEntry {
  return {
    id: dbId,
    dbId,
    type: "marker",
    data: {
      type: "roundabouts",
      label: dbId,
      lat,
      lng,
      radius: 100,
      roundaboutsTypeKey,
    },
  } as LayerEntry
}

function roundaboutsModalResult(roundaboutsTypeKey = "city_center") {
  return {
    type: "roundabouts",
    label: "City Center",
    radius: 100,
    roundaboutsTypeKey,
    numberingOutsideBandM: 100,
  }
}

// ─── SETUP ────────────────────────────────────────────────────────────────────

beforeEach(async () => {
  setActivePinia(createPinia())

  mockFeaturesStoreAdd.mockReset()
  mockDisableDraw.mockReset()
  mockOpenModal.mockReset()
  mockSaveToDatabase.mockReset()
  mockRefreshLayers.mockReset()
  mockUpdateEndpoints.mockReset()
  mockToast.mockReset()
  mockBuildDrawControl.mockReset()

  await loadModule()
  resetDrawState()
})

// ─── TESTS ────────────────────────────────────────────────────────────────────

describe("completeDrawingWithGeometry", () => {
  it("rejects a house entrance drawn at (0,0)", async () => {
    setDrawingPhase(houseEntrancesPhase)

    await completeDrawingWithGeometry({ type: "Point", coordinates: [0, 0] }, "point", {})

    expect(mockOpenModal).not.toHaveBeenCalled()
    expect(mockFeaturesStoreAdd).not.toHaveBeenCalled()
    expect(mockToast).toHaveBeenCalledWith("alert_entrance_invalid_position", "error")
  })

  it("returns early when no drawing phase is set", async () => {
    await completeDrawingWithGeometry(polygonGeometry(), "polygon", {})
    expect(mockOpenModal).not.toHaveBeenCalled()
    expect(mockSaveToDatabase).not.toHaveBeenCalled()
  })

  it("saves a polygon feature and updates the layer store", async () => {
    setDrawingPhase(areaPhase)

    mockOpenModal.mockResolvedValue({
      type: "areas",
      label: "Test Area",
      decisionNumber: "123/2024",
      decisionDate: "2024-01-15",
      areaTypeKey: "central_urban",
    })

    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "abc-123" } })

    await completeDrawingWithGeometry(polygonGeometry(), "polygon", {})

    const { useLayerStore } = await import("../../stores/layerStore")
    const layerStore = useLayerStore()

    expect(layerStore.areas).toHaveLength(1)
    expect(layerStore.areas[0].dbId).toBe("abc-123")
    expect(layerStore.areas[0].type).toBe("polygon")
    expect(layerStore.areas[0].data.label).toBe("Test Area")

    expect(mockFeaturesStoreAdd).toHaveBeenCalledTimes(1)
    expect(mockFeaturesStoreAdd).toHaveBeenCalledWith(
      expect.objectContaining({
        properties: expect.objectContaining({
          dbId: "abc-123",
          phaseKey: "areas",
          label: "Main Urban Area Test Area",
        }),
      }),
    )

    expect(mockDisableDraw).toHaveBeenCalled()
    expect(mockToast).toHaveBeenCalledWith("map_feature_saved", "success")
  })

  it("handles modal cancellation gracefully", async () => {
    setDrawingPhase(areaPhase)
    mockOpenModal.mockResolvedValue(null)

    await completeDrawingWithGeometry(polygonGeometry(), "polygon", {})

    const { useLayerStore } = await import("../../stores/layerStore")
    const layerStore = useLayerStore()

    expect(layerStore.areas).toHaveLength(0)
    expect(mockFeaturesStoreAdd).not.toHaveBeenCalled()
    expect(mockBuildDrawControl).toHaveBeenCalled()
  })

  it("shows error toast on save failure", async () => {
    setDrawingPhase(areaPhase)

    mockOpenModal.mockResolvedValue({
      label: "Failed Area",
      decisionNumber: "",
      decisionDate: "",
    })

    mockSaveToDatabase.mockResolvedValue({ ok: false, error: "Server error" })

    await completeDrawingWithGeometry(polygonGeometry(), "polygon", {})

    const { useLayerStore } = await import("../../stores/layerStore")
    const layerStore = useLayerStore()

    expect(layerStore.areas).toHaveLength(0)
    expect(mockFeaturesStoreAdd).not.toHaveBeenCalled()
    expect(mockToast).toHaveBeenCalledWith("map_save_failed", "error")
  })

  it("saves a road LineString and updates the layer store", async () => {
    const roadsPhase = (await import("../../phases")).PHASES[3]
    setDrawingPhase(roadsPhase)

    mockOpenModal.mockResolvedValue({
      label: "Main Road",
      decisionNumber: "",
      decisionDate: "",
      roadTypeKey: "main_road",
    })

    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "road-1" } })

    const lineGeometry: GeoJSON.LineString = {
      type: "LineString",
      coordinates: [
        [1, 2],
        [3, 4],
      ],
    }

    await completeDrawingWithGeometry(lineGeometry, "polyline", {})

    const { useLayerStore } = await import("../../stores/layerStore")
    const layerStore = useLayerStore()

    expect(layerStore.roads).toHaveLength(1)
    expect(layerStore.roads[0].dbId).toBe("road-1")
    expect(layerStore.roads[0].type).toBe("line")
    expect(layerStore.roads[0].data.label).toBe("Main Road")

    expect(mockUpdateEndpoints).toHaveBeenCalled()
  })

  it("rejects a second plain city center in the same urban area", async () => {
    setDrawingPhase(roundaboutsPhase)
    const { useLayerStore } = await import("../../stores/layerStore")
    const store = useLayerStore()
    store.addFeature("areas", urbanArea("area-1", 0, 0))
    store.addFeature("roundabouts", roundabouts("cc-existing", 0, 0))

    mockOpenModal.mockResolvedValue(roundaboutsModalResult())
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "cc-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(0.5, 0.5), "circle", {})

    expect(mockToast).toHaveBeenCalledWith("map_plain_roundabouts_exists", "error")
    expect(mockSaveToDatabase).not.toHaveBeenCalled()
  })

  it("rejects a plain city center outside every urban area", async () => {
    setDrawingPhase(roundaboutsPhase)
    const { useLayerStore } = await import("../../stores/layerStore")
    useLayerStore().addFeature("areas", urbanArea("area-1", 0, 0))

    mockOpenModal.mockResolvedValue(roundaboutsModalResult())
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "cc-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(5, 5), "circle", {})

    expect(mockToast).toHaveBeenCalledWith("alert_plain_roundabouts_must_be_in_urban_area", "error")
    expect(mockSaveToDatabase).not.toHaveBeenCalled()
  })

  it("allows one plain city center in each urban area", async () => {
    setDrawingPhase(roundaboutsPhase)
    const { useLayerStore } = await import("../../stores/layerStore")
    const store = useLayerStore()
    store.addFeature("areas", urbanArea("area-1", 0, 0))
    store.addFeature("areas", urbanArea("area-2", 10, 10))
    store.addFeature("roundabouts", roundabouts("cc-existing", 0, 0))

    mockOpenModal.mockResolvedValue(roundaboutsModalResult())
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "cc-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(10, 10), "circle", {})

    expect(mockSaveToDatabase).toHaveBeenCalledTimes(1)
    expect(mockToast).not.toHaveBeenCalledWith("map_plain_roundabouts_exists", "error")
  })

  it("does not count a roundabout toward the plain city-center slot", async () => {
    setDrawingPhase(roundaboutsPhase)
    const { useLayerStore } = await import("../../stores/layerStore")
    const store = useLayerStore()
    store.addFeature("areas", urbanArea("area-1", 0, 0))
    store.addFeature("roundabouts", roundabouts("ra-existing", 0, 0, "roundabout"))

    mockOpenModal.mockResolvedValue(roundaboutsModalResult())
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "cc-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(0.5, 0.5), "circle", {})

    expect(mockSaveToDatabase).toHaveBeenCalledTimes(1)
  })

  it("allows a roundabout alongside a plain city center in the same urban area", async () => {
    setDrawingPhase(roundaboutsPhase)
    const { useLayerStore } = await import("../../stores/layerStore")
    const store = useLayerStore()
    store.addFeature("areas", urbanArea("area-1", 0, 0))
    store.addFeature("roundabouts", roundabouts("cc-existing", 0, 0))

    mockOpenModal.mockResolvedValue(roundaboutsModalResult("roundabout"))
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "ra-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(0.5, 0.5), "circle", {})

    expect(mockSaveToDatabase).toHaveBeenCalledTimes(1)
  })

  it("allows a second roundabout in the same urban area", async () => {
    setDrawingPhase(roundaboutsPhase)
    const { useLayerStore } = await import("../../stores/layerStore")
    const store = useLayerStore()
    store.addFeature("areas", urbanArea("area-1", 0, 0))
    store.addFeature("roundabouts", roundabouts("ra-existing", 0, 0, "roundabout"))

    mockOpenModal.mockResolvedValue(roundaboutsModalResult("roundabout"))
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "ra-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(0.5, 0.5), "circle", {})

    expect(mockSaveToDatabase).toHaveBeenCalledTimes(1)
  })

  it("stores a newly added city center as a filled Polygon named City Center", async () => {
    setDrawingPhase(roundaboutsPhase)
    const { useLayerStore } = await import("../../stores/layerStore")
    useLayerStore().addFeature("areas", urbanArea("area-1", 0, 0))

    mockOpenModal.mockResolvedValue(roundaboutsModalResult())
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "cc-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(0.5, 0.5), "circle", {})

    expect(mockFeaturesStoreAdd).toHaveBeenCalledTimes(1)
    const added = mockFeaturesStoreAdd.mock.calls[0][0]
    expect(added.geometry.type).toBe("Polygon")
    expect(added.properties).toMatchObject({
      dbId: "cc-new",
      phaseKey: "roundabouts",
      geomType: "Polygon",
      label: "City Center",
      fillOpacity: ROUNDABOUTS_CONFIG.cityCenterFillOpacity,
    })
  })

  it("stores a newly added roundabout as a hollow LineString", async () => {
    setDrawingPhase(roundaboutsPhase)

    mockOpenModal.mockResolvedValue(roundaboutsModalResult("roundabout"))
    mockSaveToDatabase.mockResolvedValue({ ok: true, data: { id: "ra-new" } })

    await completeDrawingWithGeometry(roundaboutsPoint(0.5, 0.5), "circle", {})

    expect(mockFeaturesStoreAdd).toHaveBeenCalledTimes(1)
    const added = mockFeaturesStoreAdd.mock.calls[0][0]
    expect(added.geometry.type).toBe("LineString")
    expect(added.properties).toMatchObject({
      dbId: "ra-new",
      phaseKey: "roundabouts",
      geomType: "LineString",
      label: "Roundabout",
    })
    expect(added.properties.fillOpacity).toBeUndefined()
  })
})

describe("normalizeGeometry", () => {
  it("returns LineString from LineString input unchanged", () => {
    const geom: GeoJSON.LineString = {
      type: "LineString",
      coordinates: [
        [1, 2],
        [3, 4],
      ],
    }
    const result = normalizeGeometry(geom, "polyline")
    expect(result.type).toBe("LineString")
  })

  it("converts Polygon to LineString for polyline draw type", () => {
    const geom: GeoJSON.Polygon = {
      type: "Polygon",
      coordinates: [
        [
          [1, 2],
          [3, 4],
          [1, 2],
        ],
      ],
    }
    const result = normalizeGeometry(geom, "polyline")
    expect(result.type).toBe("LineString")
  })

  it("converts LineString to Polygon for polygon draw type", () => {
    const geom: GeoJSON.LineString = {
      type: "LineString",
      coordinates: [
        [1, 2],
        [3, 4],
        [5, 6],
        [1, 2],
      ],
    }
    const result = normalizeGeometry(geom, "polygon")
    expect(result.type).toBe("Polygon")
  })
})
