import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore } from "../../stores/layerStore"
import { PHASES } from "../../phases"
import type { LayerEntry } from "../../types"

const { mockOpenModal, mockPrepareModalExtras, mockGetRoadSide, mockShowToast } = vi.hoisted(
  () => ({
    mockOpenModal: vi.fn(),
    mockPrepareModalExtras: vi.fn().mockResolvedValue(undefined),
    mockGetRoadSide: vi.fn(),
    mockShowToast: vi.fn(),
  }),
)

vi.mock("../../stores/modalStore", async (importOriginal) => {
  const orig = await importOriginal<typeof import("../../stores/modalStore")>()
  return { ...orig, openModal: mockOpenModal }
})
vi.mock("../features/feature-modal", () => ({ prepareModalExtras: mockPrepareModalExtras }))
vi.mock("../../lib/validation", () => ({ getRoadSide: mockGetRoadSide }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast }))

let mod: typeof import("./draw-modal")

function roadEntry(overrides: Partial<LayerEntry> = {}): LayerEntry {
  return {
    id: "road_1",
    dbId: "road-1",
    type: "line",
    data: {
      type: "roads",
      label: "Rue 1",
      decisionNumber: "",
      decisionDate: "",
      roadTypeKey: "street",
    },
    ...overrides,
  } as unknown as LayerEntry
}

function mainEntranceEntry(overrides: Partial<LayerEntry> = {}): LayerEntry {
  return {
    id: "ent_1",
    dbId: "main-1",
    type: "marker",
    data: {
      type: "houseEntrances",
      label: "BIS01",
      entranceTypeKey: "main_entrance",
      mainEntranceDbId: "main-1",
      mainEntranceLabel: "MAIN",
      bisNumber: 1,
      lat: 36.5,
      lng: 127.5,
    },
    ...overrides,
  } as unknown as LayerEntry
}

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  mockOpenModal.mockResolvedValue(null)
  mockGetRoadSide.mockResolvedValue({ side: "right" })
  mod = await import("./draw-modal")
})

describe("openModalForFeature", () => {
  it("returns null and notifies when a house entrance has no reference", async () => {
    const result = await mod.openModalForFeature(PHASES[4], "feat_1", {
      type: "Point",
      coordinates: [1, 2],
    })
    expect(mockShowToast).toHaveBeenCalledWith("alert_no_reference_set", "error")
    expect(result).toBeNull()
  })

  it("opens a secondary entrance modal from a reference entrance", async () => {
    const appStore = useAppStore()
    appStore.$patch({ referenceEntranceDbId: "main-1" })
    useLayerStore().addFeature("houseEntrances", mainEntranceEntry())

    const result = await mod.openModalForFeature(PHASES[4], "feat_1", {
      type: "Point",
      coordinates: [1, 2],
    })

    expect(result).toEqual(
      expect.objectContaining({
        type: "houseEntrances",
        label: "BIS01",
        entranceTypeKey: "secondary_entrance",
        mainEntranceDbId: "main-1",
        mainEntranceLabel: "BIS01",
        bisNumber: 1,
      }),
    )
    expect(mockOpenModal).not.toHaveBeenCalled()
  })

  it("toasts when the reference entrance is missing", async () => {
    const appStore = useAppStore()
    appStore.$patch({ referenceEntranceDbId: "missing" })
    const result = await mod.openModalForFeature(PHASES[4], "feat_1", {
      type: "Point",
      coordinates: [1, 2],
    })
    expect(mockShowToast).toHaveBeenCalledWith("alert_ref_entrance_not_found", "error")
    expect(result).toBeNull()
  })

  it("opens a main entrance modal and computes the road side", async () => {
    const appStore = useAppStore()
    appStore.$patch({ referenceRoadDbId: "road-1" })
    useLayerStore().addFeature("roads", roadEntry())

    const result = await mod.openModalForFeature(PHASES[4], "feat_1", {
      type: "Point",
      coordinates: [127.5, 36.5],
    })

    expect(mockGetRoadSide).toHaveBeenCalledWith("road-1", 36.5, 127.5)
    expect(result).toEqual(
      expect.objectContaining({
        type: "houseEntrances",
        label: "?",
        entranceTypeKey: "main_entrance",
        roadDbId: "road-1",
        roadLabel: "Rue 1",
        side: "right",
        entranceNumber: undefined,
      }),
    )
  })

  it("defaults the road side to left when getRoadSide returns nothing", async () => {
    const appStore = useAppStore()
    appStore.$patch({ referenceRoadDbId: "road-1" })
    useLayerStore().addFeature("roads", roadEntry())
    mockGetRoadSide.mockResolvedValue(undefined)

    const result = await mod.openModalForFeature(PHASES[4], "feat_1", {
      type: "Point",
      coordinates: [127.5, 36.5],
    })
    expect(result).toEqual(expect.objectContaining({ side: "left" }))
  })

  it("captures the radius for a cityCenter point and calls prepareModalExtras", async () => {
    mockOpenModal.mockResolvedValue({ type: "cityCenter", label: "CC" })
    const geometry = {
      type: "Point",
      coordinates: [127.5, 36.5],
      radius: 250,
    } as GeoJSON.Point & { radius?: number }
    const result = await mod.openModalForFeature(PHASES[2], "feat_1", geometry as GeoJSON.Geometry)
    expect(mockOpenModal).toHaveBeenCalledWith(2, "feat_1", { radius: 250 })
    expect(mockPrepareModalExtras).toHaveBeenCalled()
    expect(result).toEqual({ type: "cityCenter", label: "CC" })
  })

  it("opens a non-city-center modal without a radius", async () => {
    mockOpenModal.mockResolvedValue({ type: "areas", label: "A1" })
    const result = await mod.openModalForFeature(PHASES[0], "feat_1", {
      type: "Polygon",
      coordinates: [
        [
          [0, 0],
          [0, 1],
          [1, 1],
          [0, 0],
        ],
      ],
    })
    expect(mockOpenModal).toHaveBeenCalledWith(0, "feat_1", undefined)
    expect(mockPrepareModalExtras).toHaveBeenCalled()
    expect(result).toEqual({ type: "areas", label: "A1" })
  })
})
