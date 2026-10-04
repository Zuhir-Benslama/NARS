import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import { GEN_CONFIG, EDIT_CONFIG, MAP_CONFIG } from "../../config"
import type { GenerateRoadsResponse } from "../../api/drafts"

const {
  mockCreateGenerationJob,
  mockGetGenerationJob,
  mockUploadRaster,
  mockCancelGenerationJob,
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
  mockCreateGenerationJob: vi.fn(),
  mockGetGenerationJob: vi.fn(),
  mockUploadRaster: vi.fn(),
  mockCancelGenerationJob: vi.fn(),
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
  generateRoadsFromDraftIds: mockGenerateRoads,
  listDrafts: mockListDrafts,
}))
vi.mock("../../api/generation", async () => {
  const actual =
    await vi.importActual<typeof import("../../api/generation")>("../../api/generation")
  return {
    ...actual,
    createGenerationJob: mockCreateGenerationJob,
    getGenerationJob: mockGetGenerationJob,
    uploadGenerationChunkRaster: mockUploadRaster,
    cancelGenerationJob: mockCancelGenerationJob,
  }
})
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
let splitBoundsAtZoom: any
let gridBounds: any
let toGenerationGrid: any

/** The bounds the seeded urban area (a single point) produces at zoom 18. */
const AREA_BOUNDS = { minLon: 3.0, minLat: 36.75, maxLon: 3.0, maxLat: 36.75 }
const CREATED_AT = "2026-01-01T00:00:00Z"

const TILE = {
  blob: new Blob(["x"]),
  bounds: AREA_BOUNDS,
  width: 256,
  height: 256,
}

const EMPTY_RESULT: GenerateRoadsResponse = {
  dropped: 0,
  created: [],
  breakdown: {
    tooShort: 0,
    lowConfidence: 0,
    excessiveTurnAngle: 0,
    outsideUrbanArea: 0,
    tooClose: 0,
    invalidGeometry: 0,
  },
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

/** A job in the given state whose chunks mirror exactly the grids the flow splits. */
function jobView(overrides: Record<string, unknown> = {}) {
  const grids = splitBoundsAtZoom(AREA_BOUNDS, MAP_CONFIG.tileMaxZoomSatellite)
  const chunks = grids.map(
    (g: { zoom: number; x0: number; y0: number; width: number; height: number }, i: number) => ({
      id: `chunk-${i}`,
      chunkKey: String(i),
      zoom: g.zoom,
      x0: g.x0,
      y0: g.y0,
      width: g.width,
      height: g.height,
      minLon: null,
      minLat: null,
      maxLon: null,
      maxLat: null,
      status: "awaiting_raster",
      attempts: 0,
      error: null,
      createdAt: CREATED_AT,
      updatedAt: null,
    }),
  )
  return {
    id: "job-1",
    communeId: 42,
    status: "active",
    stage: "segment",
    totalChunks: grids.length,
    doneChunks: 0,
    progress: 0,
    draftIds: [],
    error: null,
    createdAt: CREATED_AT,
    updatedAt: null,
    chunks,
    result: null,
    ...overrides,
  }
}

/** Creates a done job from the same grid split, with the given result payload. */
function doneJobView(result = EMPTY_RESULT, extra: Record<string, unknown> = {}) {
  const base = jobView()
  return {
    ...base,
    status: "done",
    stage: "accept",
    doneChunks: base.totalChunks,
    progress: 1,
    result,
    chunks: base.chunks.map((c: { id: string }) => ({ ...c, status: "done" })),
    ...extra,
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
  mockListDrafts.mockResolvedValue([])
  mockGenerateRoads.mockResolvedValue(EMPTY_RESULT)
  mockRenderTile.mockResolvedValue(TILE)

  const satMod = await import("./satellite-tiler")
  splitBoundsAtZoom = satMod.splitBoundsAtZoom
  gridBounds = satMod.gridBounds

  const generationApi = await import("../../api/generation")
  toGenerationGrid = generationApi.toGenerationGrid

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
    expect(mockCreateGenerationJob).not.toHaveBeenCalled()
  })

  it("informs and stops when no urban area is drawn", async () => {
    const result = await generateRoadsFromUrbanAreas()
    expect(result).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_no_urban_area", "info")
    expect(mockCreateGenerationJob).not.toHaveBeenCalled()
  })

  it("starts the progress bar and does not re-run while active", async () => {
    const store = useGenerationStore()
    seedUrbanArea()
    let releaseCreate!: () => void
    mockCreateGenerationJob.mockImplementationOnce(
      () => new Promise((resolve) => (releaseCreate = () => resolve(jobView()))),
    )
    mockGetGenerationJob.mockResolvedValue(doneJobView({ ...EMPTY_RESULT, created: [road("r1")] }))

    const first = generateRoadsFromUrbanAreas()
    expect(store.active).toBe(true)
    expect(store.progress).toBe(0)

    const second = await generateRoadsFromUrbanAreas()
    expect(second).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_in_progress", "warning")

    releaseCreate()
    await first
  })

  it("creates one job per grid split, uploads every chunk, then applies the result", async () => {
    const created = [road("r1"), road("r2")]
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(
      doneJobView({
        dropped: 1,
        created,
        breakdown: {
          tooShort: 1,
          lowConfidence: 0,
          excessiveTurnAngle: 0,
          outsideUrbanArea: 0,
          tooClose: 0,
          invalidGeometry: 0,
        },
      }),
    )

    const result = await generateRoadsFromUrbanAreas()

    const grids = splitBoundsAtZoom(AREA_BOUNDS, MAP_CONFIG.tileMaxZoomSatellite)
    expect(mockCreateGenerationJob).toHaveBeenCalledTimes(1)
    expect(mockCreateGenerationJob).toHaveBeenCalledWith(
      42,
      grids.map((grid: any, index: number) =>
        toGenerationGrid(String(index), grid.zoom, grid, gridBounds(grid)),
      ),
    )

    expect(mockUploadRaster).toHaveBeenCalledTimes(grids.length)
    grids.forEach((_grid: unknown, i: number) => {
      expect(mockUploadRaster).toHaveBeenCalledWith("job-1", `chunk-${i}`, TILE.blob, `${i}.jpg`)
    })

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
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(doneJobView({ ...EMPTY_RESULT, created: [road("r1")] }))

    await generateRoadsFromUrbanAreas()
    expect(useGenerationStore().active).toBe(true)

    vi.advanceTimersByTime(GEN_CONFIG.completeSettleMs + 1)
    expect(useGenerationStore().active).toBe(false)
  })

  it("reports success with zero counts when nothing was detected and no drafts remain", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(doneJobView())
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

  it("rebuilds from the commune's pending drafts when the run created none", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(doneJobView())
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
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_reusing_drafts", "info")
    expect(mockShowToast).not.toHaveBeenCalledWith("gen_roads_no_detections", "info")
    expect(useLayerStore().$state.roads).toHaveLength(1)
  })

  it("merges the fallback acceptance counts into the report", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(doneJobView())
    mockListDrafts.mockResolvedValue([{ id: "d-pending-1" }] as any)
    mockGenerateRoads.mockResolvedValue({
      created: [road("r1")],
      dropped: 2,
      breakdown: {
        tooShort: 1,
        lowConfidence: 1,
        excessiveTurnAngle: 0,
        outsideUrbanArea: 0,
        tooClose: 0,
        invalidGeometry: 0,
      },
    })

    const result = await generateRoadsFromUrbanAreas()

    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_done_breakdown", "success")
    expect(result).toEqual({ created: 1, dropped: 2 })
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_done", "success")
  })

  it("pages past the 500-draft cap when collecting pending road drafts", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(doneJobView())
    const fullPage = Array.from({ length: 500 }, (_, i) => ({ id: `d-${i}` }))
    mockListDrafts
      .mockResolvedValueOnce(fullPage as any)
      .mockResolvedValueOnce([{ id: "d-500" }] as any)
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

  it("shows the accepting stage while the job runs the acceptance pass", async () => {
    vi.useFakeTimers()
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    // Initial fetch → accepting; first re-fetch → accepting again (so the
    // progress bar transitions to the detect milestone); second re-fetch → done.
    mockGetGenerationJob
      .mockResolvedValueOnce(jobView({ status: "accepting", stage: "accept" }))
      .mockResolvedValueOnce(jobView({ status: "accepting", stage: "accept" }))
      .mockResolvedValue(doneJobView({ ...EMPTY_RESULT, created: [road("r1")] }))

    const pending = generateRoadsFromUrbanAreas()
    await vi.advanceTimersByTimeAsync(2_500)

    const generation = useGenerationStore()
    expect(generation.stage).toBe("gen_roads_stage_detect")
    expect(generation.progress).toBe(GEN_CONFIG.progressMilestones.detect)

    await vi.advanceTimersByTimeAsync(2_500)
    await pending
    expect(generation.stage).toBe("gen_roads_stage_done")
  })

  it("skips roads with too few coordinates and does not duplicate dbIds", async () => {
    const created = [
      road("r3"),
      { ...road("r4"), data: { coordinates: [{ lat: 36.7, lng: 3.0 }] } },
    ]
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(doneJobView({ ...EMPTY_RESULT, created }))

    const result = await generateRoadsFromUrbanAreas()

    expect(result).toEqual({ created: 2, dropped: 0 })
    expect(useLayerStore().$state.roads).toHaveLength(1)
    expect(useFeaturesStore().getAll()).toHaveLength(1)
    expect(mockImportFeaturesIntoGeoman.mock.calls[0][0]).toHaveLength(1)
  })

  it("warns when generated roads could not be imported into geoman", async () => {
    seedUrbanArea()
    mockImportFeaturesIntoGeoman.mockResolvedValueOnce(0)
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(doneJobView({ ...EMPTY_RESULT, created: [road("r1")] }))

    await generateRoadsFromUrbanAreas()

    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_geoman_failed", "warning")
  })

  it("informs when the job ends cancelled", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(jobView({ status: "cancelled", stage: "segment" }))

    const result = await generateRoadsFromUrbanAreas()

    expect(result).toEqual({ created: 0, dropped: 0 })
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_cancelled", "info")
    expect(mockGenerateRoads).not.toHaveBeenCalled()
    expect(useGenerationStore().progress).toBe(100)
  })

  it("cancels the partially uploaded job when a chunk upload fails", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockUploadRaster.mockRejectedValueOnce(new Error("network"))
    mockGetGenerationJob.mockResolvedValue(doneJobView())

    const result = await generateRoadsFromUrbanAreas()

    expect(result).toBeNull()
    expect(mockCancelGenerationJob).toHaveBeenCalledTimes(1)
    expect(mockCancelGenerationJob).toHaveBeenCalledWith("job-1")
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_failed", "error")
    expect(useGenerationStore().active).toBe(false)
  })

  it("shows an error toast when job creation fails", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockRejectedValue(new Error("boom"))

    const result = await generateRoadsFromUrbanAreas()

    expect(result).toBeNull()
    expect(mockCancelGenerationJob).not.toHaveBeenCalled()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_failed", "error")
    expect(mockDebugError).toHaveBeenCalled()
    expect(useGenerationStore().active).toBe(false)
  })

  it("never requests the districts phase — it has its own action", async () => {
    seedUrbanArea()
    mockCreateGenerationJob.mockResolvedValue(jobView())
    mockGetGenerationJob.mockResolvedValue(
      doneJobView(EMPTY_RESULT, {
        districtsResult: {
          districts: [
            { draftId: "d1", areaM2: 1200, lat: 36.7, lng: 2.9 },
            { draftId: "d2", areaM2: 900, lat: 36.71, lng: 2.91 },
          ],
          absorbedSlivers: 1,
          primaryRoadCount: 2,
          urbanAreaCount: 1,
        },
      }),
    )

    const result = await generateRoadsFromUrbanAreas()

    // No districts flag, and a districtsResult present on the job is ignored:
    // roads generation must stay roads-only.
    expect(mockCreateGenerationJob).toHaveBeenCalledWith(42, expect.any(Array))
    expect(result).toEqual({ created: 0, dropped: 0 })
    expect(mockShowToast).not.toHaveBeenCalledWith("gen_districts_done", "success")
    expect(mockShowToast).not.toHaveBeenCalledWith("gen_districts_none", "info")
  })
})
