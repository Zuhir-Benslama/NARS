import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import { GEN_CONFIG, EDIT_CONFIG } from "../../config"

const {
  mockSegmentTile,
  mockGenerateRoads,
  mockListDrafts,
  mockRenderTile,
  mockShowToast,
  mockGetUserMessageKey,
  mockDebugError,
  mockRefreshLayerVisibility,
  mockUpdateEndpointMarkers,
  mockImportFeaturesIntoGeoman,
} = vi.hoisted(() => ({
  mockSegmentTile: vi.fn(),
  mockGenerateRoads: vi.fn(),
  mockListDrafts: vi.fn(),
  mockRenderTile: vi.fn(),
  mockShowToast: vi.fn(),
  mockGetUserMessageKey: vi.fn(() => "err_unknown"),
  mockDebugError: vi.fn(),
  mockRefreshLayerVisibility: vi.fn(),
  mockUpdateEndpointMarkers: vi.fn(),
  mockImportFeaturesIntoGeoman: vi.fn().mockResolvedValue(1),
}))

vi.mock("../../api/drafts", () => ({
  segmentTile: mockSegmentTile,
  generateRoadsFromDraftIds: mockGenerateRoads,
  listDrafts: mockListDrafts,
}))
vi.mock("./satellite-tiler", async () => {
  const actual = await vi.importActual<typeof import("./satellite-tiler")>("./satellite-tiler")
  return { ...actual, renderSatelliteGrid: mockRenderTile }
})
vi.mock("./geoman-import", () => ({ importFeaturesIntoGeoman: mockImportFeaturesIntoGeoman }))
vi.mock("../../i18n", () => ({ t: (key: string) => key }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast }))
vi.mock("../../lib/errors", () => ({ getUserMessageKey: mockGetUserMessageKey }))
vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))
vi.mock("../rendering/labels", () => ({ refreshLayerVisibility: mockRefreshLayerVisibility }))
vi.mock("../roads/road-directions", () => ({ updateEndpointMarkers: mockUpdateEndpointMarkers }))

let useAppStore: any
let useLayerStore: any
let useFeaturesStore: any
let useGenerationStore: any
let generateRoadsFromUrbanAreas: any
let urbanAreaBounds: any

const TILE = {
  blob: new Blob(["x"]),
  bounds: { minLon: 2.9, minLat: 36.7, maxLon: 3.1, maxLat: 36.8 },
  width: 256,
  height: 256,
}

function seedUrbanArea(dbId = "area-1", coords = [{ lat: 36.75, lng: 3.0 }]) {
  useLayerStore().addFeature("areas", {
    id: `feat_${dbId}`,
    dbId,
    data: {
      type: "areas",
      label: "Zone A",
      coordinates: coords,
      decisionNumber: "01",
      decisionDate: "2024-01-01",
      areaTypeKey: "central_urban",
    },
    type: "polygon",
  })
}

function road(id: string) {
  return {
    dbId: id,
    layer: "street",
    label: "",
    data: {
      type: "road",
      coordinates: [
        { lat: 36.75, lng: 3.0 },
        { lat: 36.76, lng: 3.01 },
      ],
    },
  }
}

afterEach(() => {
  vi.useRealTimers()
})

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())

  const draftsMod = await import("../../api/drafts")
  mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 0, draftIds: [] })
  mockListDrafts.mockResolvedValue([])
  mockGenerateRoads.mockResolvedValue({
    created: [],
    dropped: 0,
    breakdown: {
      tooShort: 0,
      lowConfidence: 0,
      excessiveTurnAngle: 0,
      outsideUrbanArea: 0,
      tooClose: 0,
      invalidGeometry: 0,
    },
  })
  mockRenderTile.mockResolvedValue(TILE)

  const appMod = await import("../../stores/appStore")
  useAppStore = appMod.useAppStore
  useAppStore().setUser({
    id: 1,
    role: "commune_user",
    commune: { id: 42, name_fr: "Alger", name_ar: "", latitude: null, longitude: null },
  } as any)

  const layerMod = await import("../../stores/layerStore")
  useLayerStore = layerMod.useLayerStore
  const featuresMod = await import("../../stores/featuresStore")
  useFeaturesStore = featuresMod.useFeaturesStore
  const generationMod = await import("../../stores/generationStore")
  useGenerationStore = generationMod.useGenerationStore

  const mod = await import("./generate-roads")
  generateRoadsFromUrbanAreas = mod.generateRoadsFromUrbanAreas
  urbanAreaBounds = mod.urbanAreaBounds

  void draftsMod
})

describe("urbanAreaBounds", () => {
  it("returns null when no urban areas are drawn", () => {
    expect(urbanAreaBounds()).toBeNull()
  })

  it("ignores non-urban area types", () => {
    useLayerStore().addFeature("areas", {
      id: "feat_x",
      dbId: "x",
      data: {
        type: "areas",
        label: "Scattered",
        coordinates: [{ lat: 36.7, lng: 3.0 }],
        decisionNumber: "1",
        decisionDate: "2024-01-01",
        areaTypeKey: "scattered",
      },
      type: "polygon",
    })
    expect(urbanAreaBounds()).toBeNull()
  })

  it("computes the union bounds of urban areas", () => {
    seedUrbanArea("a", [{ lat: 36.7, lng: 3.0 }])
    seedUrbanArea("b", [{ lat: 36.8, lng: 3.1 }])
    expect(urbanAreaBounds()).toEqual({ minLon: 3.0, minLat: 36.7, maxLon: 3.1, maxLat: 36.8 })
  })
})

describe("generateRoadsFromUrbanAreas", () => {
  it("warns and stops when the account has no commune", async () => {
    useAppStore().setUser(null)
    seedUrbanArea()
    const result = await generateRoadsFromUrbanAreas()
    expect(result).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_no_commune", "error")
    expect(mockSegmentTile).not.toHaveBeenCalled()
  })

  it("informs and stops when no urban area is drawn", async () => {
    const result = await generateRoadsFromUrbanAreas()
    expect(result).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_no_urban_area", "info")
    expect(mockSegmentTile).not.toHaveBeenCalled()
  })

  it("starts the progress bar and does not re-run while active", async () => {
    const store = useGenerationStore()
    seedUrbanArea()
    let releaseSegment!: () => void
    mockSegmentTile.mockImplementationOnce(
      () =>
        new Promise(
          (resolve) => (releaseSegment = () => resolve({ roadCount: 1, draftIds: ["d1"] })),
        ),
    )

    const first = generateRoadsFromUrbanAreas()
    expect(store.active).toBe(true)
    expect(store.progress).toBe(5)

    const second = await generateRoadsFromUrbanAreas()
    expect(second).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_in_progress", "warning")

    releaseSegment()
    mockGenerateRoads.mockResolvedValue({ created: [road("r1")], dropped: 0 })
    await first
  })

  it("runs the full flow and adds created roads to the stores", async () => {
    const created = [road("r1"), road("r2")]
    seedUrbanArea()
    mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 2, draftIds: ["d1", "d2"] })
    mockGenerateRoads.mockResolvedValue({
      created,
      dropped: 1,
      breakdown: {
        tooShort: 1,
        lowConfidence: 0,
        excessiveTurnAngle: 0,
        outsideUrbanArea: 0,
        tooClose: 0,
        invalidGeometry: 0,
      },
    })

    const result = await generateRoadsFromUrbanAreas()

    expect(mockSegmentTile).toHaveBeenCalledWith({
      communeId: 42,
      tile: TILE.blob,
      bounds: TILE.bounds,
    })
    expect(mockGenerateRoads).toHaveBeenCalledWith(42, ["d1", "d2"])
    expect(result).toEqual({ created: 2, dropped: 1 })
    expect(useLayerStore().$state.roads).toHaveLength(2)
    expect(useFeaturesStore().getAll()).toHaveLength(2)
    const [first] = useFeaturesStore().getAll()
    expect(first).toMatchObject({ geometry: { type: "LineString" } })
    expect(first.properties.lineWidth).toBe(EDIT_CONFIG.edgeLineWidth)
    expect(mockUpdateEndpointMarkers).toHaveBeenCalled()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_done_breakdown", "success")
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_done", "success")

    const generation = useGenerationStore()
    expect(generation.progress).toBe(100)
    expect(generation.stage).toBe("gen_roads_stage_done")

    expect(mockImportFeaturesIntoGeoman).toHaveBeenCalledTimes(1)
    const imported = mockImportFeaturesIntoGeoman.mock.calls[0][0]
    expect(imported.map((e: { dbId: string }) => e.dbId)).toEqual(["r1", "r2"])
    expect(imported[0].type).toBe("line")
  })

  it("hides the progress bar after the settle delay on success", async () => {
    vi.useFakeTimers()
    seedUrbanArea()
    mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 1, draftIds: ["d1"] })
    mockGenerateRoads.mockResolvedValue({ created: [road("r1")], dropped: 0 })

    await generateRoadsFromUrbanAreas()
    expect(useGenerationStore().active).toBe(true)

    vi.advanceTimersByTime(GEN_CONFIG.completeSettleMs + 1)
    expect(useGenerationStore().active).toBe(false)
  })

  it("reports success with zero counts when nothing was detected and no drafts remain", async () => {
    seedUrbanArea()
    mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 0, draftIds: [] })
    mockListDrafts.mockResolvedValue([])

    const result = await generateRoadsFromUrbanAreas()

    expect(result).toEqual({ created: 0, dropped: 0 })
    expect(mockGenerateRoads).not.toHaveBeenCalled()
    expect(mockListDrafts).toHaveBeenCalledWith({
      communeId: 42,
      featureType: "road",
      status: "pending",
      skip: 0,
      take: 500,
    })
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_no_detections", "info")
  })

  it("rebuilds from the commune's pending drafts when segmentation finds nothing new", async () => {
    seedUrbanArea()
    mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 0, draftIds: [] })
    mockListDrafts.mockResolvedValue([{ id: "d-pending-1" }, { id: "d-pending-2" }] as any)
    mockGenerateRoads.mockResolvedValue({
      created: [road("r1")],
      dropped: 1,
      breakdown: {
        tooShort: 1,
        lowConfidence: 0,
        excessiveTurnAngle: 0,
        outsideUrbanArea: 0,
        tooClose: 0,
        invalidGeometry: 0,
      },
    })

    const result = await generateRoadsFromUrbanAreas()

    expect(mockGenerateRoads).toHaveBeenCalledWith(42, ["d-pending-1", "d-pending-2"])
    expect(result).toEqual({ created: 1, dropped: 1 })
    expect(mockShowToast).not.toHaveBeenCalledWith("gen_roads_no_detections", "info")
    expect(useLayerStore().$state.roads).toHaveLength(1)
  })

  it("pages past the 500-draft cap when collecting pending road drafts", async () => {
    seedUrbanArea()
    mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 0, draftIds: [] })
    const fullPage = Array.from({ length: 500 }, (_, i) => ({ id: `d-${i}` }))
    mockListDrafts
      .mockResolvedValueOnce(fullPage as any)
      .mockResolvedValueOnce([{ id: "d-500" }] as any)
    mockGenerateRoads.mockResolvedValue({ created: [], dropped: 0 })

    const result = await generateRoadsFromUrbanAreas()

    expect(mockGenerateRoads).toHaveBeenCalledWith(42, [
      ...Array.from({ length: 500 }, (_, i) => `d-${i}`),
      "d-500",
    ])
    expect(result).toEqual({ created: 0, dropped: 0 })
    expect(mockListDrafts).toHaveBeenNthCalledWith(2, {
      communeId: 42,
      featureType: "road",
      status: "pending",
      skip: 500,
      take: 500,
    })
  })

  it("skips roads with too few coordinates and does not duplicate dbIds", async () => {
    const created = [
      road("r3"),
      { ...road("r4"), data: { coordinates: [{ lat: 36.7, lng: 3.0 }] } },
    ]
    seedUrbanArea()
    mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 2, draftIds: ["d1", "d2"] })
    mockGenerateRoads.mockResolvedValue({ created, dropped: 0 })

    const result = await generateRoadsFromUrbanAreas()

    expect(result).toEqual({ created: 2, dropped: 0 })
    expect(useLayerStore().$state.roads).toHaveLength(1)
    expect(useFeaturesStore().getAll()).toHaveLength(1)
    expect(mockImportFeaturesIntoGeoman.mock.calls[0][0]).toHaveLength(1)
  })

  it("warns when generated roads could not be imported into geoman", async () => {
    const created = [road("r1")]
    seedUrbanArea()
    mockImportFeaturesIntoGeoman.mockResolvedValueOnce(0)
    mockSegmentTile.mockResolvedValue({ buildingCount: 0, roadCount: 1, draftIds: ["d1"] })
    mockGenerateRoads.mockResolvedValue({ created, dropped: 0 })

    await generateRoadsFromUrbanAreas()

    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_geoman_failed", "warning")
  })

  it("shows an error toast when a step fails", async () => {
    seedUrbanArea()
    mockSegmentTile.mockRejectedValue(new Error("boom"))

    const result = await generateRoadsFromUrbanAreas()

    expect(result).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_failed", "error")
    expect(mockDebugError).toHaveBeenCalled()
    expect(useGenerationStore().active).toBe(false)
  })
})
