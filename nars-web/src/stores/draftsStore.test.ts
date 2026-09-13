import { beforeEach, describe, expect, it, vi } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { AiDraftFeatureDto } from "../api/drafts"
import { DRAFT_BUILDING_COLOR, DRAFT_ROAD_COLOR } from "../map/drafts/draft-style"

const mocks = vi.hoisted(() => {
  const setData = vi.fn()
  return {
    setData,
    debugWarn: vi.fn(),
    debugLog: vi.fn(),
    listDrafts: vi.fn(),
    ctx: undefined as unknown,
  }
})

vi.mock("../api/drafts", () => ({ listDrafts: mocks.listDrafts }))

vi.mock("../map/core/state", () => ({
  tryGetCtx: () => mocks.ctx,
  getCtx: () => mocks.ctx,
}))

vi.mock("../utils/debug", () => ({
  debugWarn: mocks.debugWarn,
  debugLog: mocks.debugLog,
}))

import { useDraftsStore } from "./draftsStore"

const ROAD_DRAFT: AiDraftFeatureDto = {
  id: "d1",
  featureType: "road",
  geometryGeoJson: '{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}',
  confidence: 0.9,
  status: "pending",
  createdAt: "2026-01-01T00:00:00Z",
}

const BUILDING_DRAFT: AiDraftFeatureDto = {
  id: "d2",
  featureType: "building",
  geometryGeoJson:
    '{"type":"Polygon","coordinates":[[[36.71,2.95],[36.72,2.95],[36.72,2.96],[36.71,2.95]]]}',
  confidence: "0.75",
  status: "pending",
  createdAt: "2026-01-01T00:00:00Z",
}

function setCtx(draftsSource: unknown = { setData: mocks.setData }): void {
  mocks.ctx = { draftsSource }
}

describe("draftsStore", () => {
  let store: ReturnType<typeof useDraftsStore>

  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    store = useDraftsStore()
    setCtx()
  })

  it("starts with empty review state", () => {
    expect(store.pending).toEqual([])
    expect(store.features).toEqual([])
    expect(store.loading).toBe(false)
    expect(store.selectedDraft).toBeNull()
  })

  it("load fetches pending drafts for the commune and maps them into styled features", async () => {
    mocks.listDrafts.mockResolvedValue([ROAD_DRAFT, BUILDING_DRAFT])

    await store.load(7)

    expect(mocks.listDrafts).toHaveBeenCalledWith({ communeId: 7, status: "pending" })
    expect(store.loading).toBe(false)
    expect(store.drafts).toHaveLength(2)
    expect(store.pending).toHaveLength(2)

    const road = store.features[0]
    expect(road.id).toBe("draft_d1")
    expect(road.geometry.type).toBe("LineString")
    expect(road.properties.draftId).toBe("d1")
    expect(road.properties.confidence).toBe(0.9)
    expect(road.properties.lineColor).toBe(DRAFT_ROAD_COLOR)
    expect(road.properties.fillColor).toBeUndefined()

    const building = store.features[1]
    expect(building.geometry.type).toBe("Polygon")
    expect(building.properties.lineColor).toBe(DRAFT_BUILDING_COLOR)
    expect(building.properties.fillColor).toBe(DRAFT_BUILDING_COLOR)

    expect(mocks.setData).toHaveBeenCalledTimes(1)
    const collection = mocks.setData.mock.calls[0][0] as GeoJSON.FeatureCollection
    expect(collection.type).toBe("FeatureCollection")
    expect(collection.features).toHaveLength(2)
  })

  it("load passes a null commune through and tolerates an empty page", async () => {
    mocks.listDrafts.mockResolvedValue([])

    await store.load(null)

    expect(mocks.listDrafts).toHaveBeenCalledWith({ communeId: null, status: "pending" })
    expect(store.features).toEqual([])
  })

  it("load logs a warning and clears loading when the fetch fails", async () => {
    mocks.listDrafts.mockRejectedValue(new Error("boom"))

    await store.load(7)

    expect(store.loading).toBe(false)
    expect(mocks.debugWarn).toHaveBeenCalled()
  })

  it("load skips drafts whose geometry cannot be parsed", async () => {
    mocks.listDrafts.mockResolvedValue([
      ROAD_DRAFT,
      { ...ROAD_DRAFT, id: "bad", geometryGeoJson: "not json" },
    ])

    await store.load(7)

    expect(store.features).toHaveLength(1)
    expect(store.features[0].properties.draftId).toBe("d1")
  })

  it("toMapFeature returns null for geometry kinds drafts never carry", () => {
    const point = { ...ROAD_DRAFT, geometryGeoJson: '{"type":"Point","coordinates":[36.7,2.9]}' }
    expect(store.toMapFeature(ROAD_DRAFT)).not.toBeNull()
    expect(store.toMapFeature(point)).toBeNull()
  })

  it("selectedDraft getter resolves the selected draft by id", () => {
    store.drafts = [ROAD_DRAFT, BUILDING_DRAFT]
    store.setSelectedDraftId("d2")
    expect(store.selectedDraft?.id).toBe("d2")
    store.setSelectedDraftId(null)
    expect(store.selectedDraft).toBeNull()
  })

  it("replaceDraftGeometry updates the draft and the matching map feature", () => {
    store.drafts = [{ ...ROAD_DRAFT }]
    store.features = store.drafts
      .map((d) => store.toMapFeature(d))
      .filter((f): f is NonNullable<typeof f> => f !== null)
    mocks.setData.mockClear()

    const newGeo = '{"type":"LineString","coordinates":[[0,0],[1,1]]}'
    store.replaceDraftGeometry("d1", newGeo)

    expect(store.drafts[0].geometryGeoJson).toBe(newGeo)
    expect(store.features[0].geometry).toMatchObject({ type: "LineString" })
    expect(mocks.setData).toHaveBeenCalledTimes(1)
  })

  it("replaceDraftGeometry is a no-op for an unknown draft", () => {
    store.drafts = [{ ...ROAD_DRAFT }]
    mocks.setData.mockClear()

    store.replaceDraftGeometry("ghost", '{"type":"LineString","coordinates":[[0,0]]}')

    expect(store.drafts[0].geometryGeoJson).toBe(ROAD_DRAFT.geometryGeoJson)
    expect(mocks.setData).not.toHaveBeenCalled()
  })

  it("replaceDraftGeometry removes the map feature when the new geometry is invalid", () => {
    store.drafts = [{ ...ROAD_DRAFT }]
    store.features = [store.toMapFeature(ROAD_DRAFT) as NonNullable<never>]
    mocks.setData.mockClear()

    store.replaceDraftGeometry("d1", "not json")

    expect(store.drafts).toHaveLength(1)
    expect(store.features).toHaveLength(0)
    expect(mocks.setData).toHaveBeenCalledTimes(1)
  })

  it("removeDraft removes the draft, its feature and clears selection when matching", () => {
    store.drafts = [{ ...ROAD_DRAFT }, { ...BUILDING_DRAFT }]
    store.features = [
      store.toMapFeature(ROAD_DRAFT) as NonNullable<never>,
      store.toMapFeature(BUILDING_DRAFT) as NonNullable<never>,
    ]
    store.setSelectedDraftId("d1")
    store.setActiveDraftEditId("d1")

    store.removeDraft("d1")

    expect(store.drafts).toHaveLength(1)
    expect(store.drafts[0].id).toBe("d2")
    expect(store.features).toHaveLength(1)
    expect(store.selectedDraftId).toBeNull()
    expect(store.activeDraftEditId).toBeNull()
  })

  it("removeDraft keeps selection when a different draft is removed", () => {
    store.drafts = [ROAD_DRAFT, BUILDING_DRAFT]
    store.setSelectedDraftId("d2")

    store.removeDraft("d1")

    expect(store.selectedDraftId).toBe("d2")
  })

  it("updateSource warns when the drafts source is not attached", () => {
    mocks.ctx = undefined

    store.updateSource()

    expect(mocks.debugWarn).toHaveBeenCalledWith(expect.stringContaining("NOT set"))
  })

  it("updateSource logs a warning when the source update throws", () => {
    setCtx({
      setData: vi.fn(() => {
        throw new Error("map is gone")
      }),
    })
    store.drafts = [ROAD_DRAFT]

    store.updateSource()

    expect(mocks.debugWarn).toHaveBeenCalledWith(
      "draftsStore.updateSource failed:",
      expect.anything(),
    )
  })

  it("setters update the selected and active draft ids", () => {
    store.setSelectedDraftId("d1")
    store.setActiveDraftEditId("d2")
    expect(store.selectedDraftId).toBe("d1")
    expect(store.activeDraftEditId).toBe("d2")
  })
})
