import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"

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
  it("returns early when no drawing phase is set", async () => {
    await completeDrawingWithGeometry(polygonGeometry(), "polygon", {})
    expect(mockOpenModal).not.toHaveBeenCalled()
    expect(mockSaveToDatabase).not.toHaveBeenCalled()
  })

  it("saves a polygon feature and updates the layer store", async () => {
    setDrawingPhase(areaPhase)

    mockOpenModal.mockResolvedValue({
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
          label: "Test Area",
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
