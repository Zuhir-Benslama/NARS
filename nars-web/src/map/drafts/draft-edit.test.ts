import { beforeEach, describe, expect, it, vi } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { AiDraftFeatureDto } from "../../api/drafts"

const mocks = vi.hoisted(() => {
  const geoman = {
    features: { importGeoJson: vi.fn(), getAll: vi.fn(), delete: vi.fn() },
    enableGlobalEditMode: vi.fn(),
  }
  return {
    geoman,
    geomanRef: { current: geoman } as { current: typeof geoman | null },
    ensureGeoman: vi.fn(),
    showToast: vi.fn(),
    updateDraftGeometry: vi.fn(),
    setActiveGeomanFeatureId: vi.fn(),
    disableEditMode: vi.fn(),
    disableCrosshair: vi.fn(),
    disableSnapping: vi.fn(),
    setSnapExclude: vi.fn(),
    setEditModeActive: vi.fn(),
    patchMarkerPointerSnap: vi.fn(),
    debugError: vi.fn(),
    debugWarn: vi.fn(),
  }
})

vi.mock("../core/state", () => ({
  getCtx: () => ({ geoman: mocks.geomanRef.current }),
  tryGetCtx: () => ({ draftsSource: { setData: vi.fn() } }),
}))

vi.mock("../map-init", () => ({ ensureGeoman: mocks.ensureGeoman }))

vi.mock("../../lib/toast", () => ({ showToast: mocks.showToast }))

vi.mock("../../api/drafts", () => ({ updateDraftGeometry: mocks.updateDraftGeometry }))

vi.mock("../edit/edit-state", () => ({
  setActiveGeomanFeatureId: mocks.setActiveGeomanFeatureId,
  disableEditMode: mocks.disableEditMode,
}))

vi.mock("../snapping/snapping", () => ({
  disableCrosshair: mocks.disableCrosshair,
  disableSnapping: mocks.disableSnapping,
  setSnapExclude: mocks.setSnapExclude,
  setEditModeActive: mocks.setEditModeActive,
}))

vi.mock("../edit/edit-snap", () => ({ patchMarkerPointerSnap: mocks.patchMarkerPointerSnap }))

vi.mock("../../utils/debug", () => ({
  debugError: mocks.debugError,
  debugWarn: mocks.debugWarn,
}))

import { useDraftsStore } from "../../stores/draftsStore"
import { useEditStore } from "../../stores/editStore"
import { cancelDraftEdit, commitDraftEdit, startDraftEdit } from "./draft-edit"

const ROAD_GEOMETRY = '{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}'
const BUILDING_GEOMETRY =
  '{"type":"Polygon","coordinates":[[[36.71,2.95],[36.72,2.95],[36.72,2.96],[36.71,2.95]]]}'

function draft(
  id: string,
  featureType: "road" | "building",
  geometryGeoJson: string,
): AiDraftFeatureDto {
  return {
    id,
    featureType,
    geometryGeoJson,
    confidence: 0.9,
    status: "pending",
    createdAt: "2026-01-01T00:00:00Z",
  }
}

function editedGeometry(id: string | null = "g1", geometry: unknown = null): void {
  useEditStore().setActiveGeomanFeatureId(id)
  const result = geometry === null ? { features: [] } : { features: [{ id, geometry }] }
  mocks.geoman.features.getAll.mockResolvedValue(result)
}

describe("startDraftEdit", () => {
  let store: ReturnType<typeof useDraftsStore>

  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    store = useDraftsStore()
    mocks.geomanRef.current = mocks.geoman
    mocks.ensureGeoman.mockResolvedValue(undefined)
    mocks.geoman.enableGlobalEditMode.mockResolvedValue(undefined)
  })

  it("loads geoman, imports the draft geometry and enters edit mode", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    mocks.geoman.features.importGeoJson.mockResolvedValue({ addedFeatures: [{ id: "g1" }] })

    await startDraftEdit("d1")

    expect(mocks.ensureGeoman).toHaveBeenCalledOnce()
    const [feature, opts] = mocks.geoman.features.importGeoJson.mock.calls[0] as [
      GeoJSON.Feature,
      unknown,
    ]
    expect(feature.geometry).toMatchObject({ type: "LineString" })
    expect(opts).toEqual({ overwrite: true })
    expect(mocks.setActiveGeomanFeatureId).toHaveBeenCalledWith("g1")
    expect(store.activeDraftEditId).toBe("d1")
    expect(store.selectedDraftId).toBe("d1")
    expect(mocks.disableCrosshair).toHaveBeenCalledOnce()
    expect(mocks.disableSnapping).toHaveBeenCalledOnce()
    expect(mocks.geoman.enableGlobalEditMode).toHaveBeenCalledOnce()
    expect(mocks.setEditModeActive).toHaveBeenCalledWith(true)
    expect(useEditStore().isEditMode).toBe(true)
    expect(mocks.setSnapExclude).toHaveBeenCalledWith(null)
    expect(mocks.patchMarkerPointerSnap).toHaveBeenCalledWith(null)
    expect(mocks.showToast).toHaveBeenCalledWith("map_edit_mode_hint", "info")
  })

  it("marks building drafts with a polygon shape", async () => {
    store.drafts = [draft("d2", "building", BUILDING_GEOMETRY)]
    mocks.geoman.features.importGeoJson.mockResolvedValue({ addedFeatures: [] })

    await startDraftEdit("d2")

    const [feature] = mocks.geoman.features.importGeoJson.mock.calls[0] as [GeoJSON.Feature]
    expect(feature.geometry).toMatchObject({ type: "Polygon" })
    expect(feature.properties).toMatchObject({ shape: "polygon", draftId: "d2" })
    expect(mocks.setActiveGeomanFeatureId).toHaveBeenCalledWith(null)
  })

  it("shows an unavailable toast when the draft is missing", async () => {
    await startDraftEdit("ghost")

    expect(mocks.geoman.features.importGeoJson).not.toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("map_draft_edit_unavailable", "error")
  })

  it("shows an unavailable toast when the geometry cannot be parsed", async () => {
    store.drafts = [draft("d1", "road", "not json")]

    await startDraftEdit("d1")

    expect(mocks.geoman.features.importGeoJson).not.toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("map_draft_edit_unavailable", "error")
  })

  it("rejects geometry kinds drafts never carry", async () => {
    store.drafts = [draft("d1", "road", '{"type":"Point","coordinates":[36.7,2.9]}')]

    await startDraftEdit("d1")

    expect(mocks.geoman.features.importGeoJson).not.toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("map_draft_edit_unavailable", "error")
  })

  it("logs and aborts when geoman cannot import the geometry", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    mocks.geoman.features.importGeoJson.mockRejectedValue(new Error("failed"))

    await startDraftEdit("d1")

    expect(mocks.debugError).toHaveBeenCalled()
    expect(store.activeDraftEditId).toBeNull()
  })

  it("is a no-op when geoman is not attached", async () => {
    mocks.geomanRef.current = null

    await startDraftEdit("d1")

    expect(mocks.showToast).not.toHaveBeenCalled()
  })
})

describe("commitDraftEdit", () => {
  let store: ReturnType<typeof useDraftsStore>

  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    store = useDraftsStore()
    mocks.geomanRef.current = mocks.geoman
    mocks.updateDraftGeometry.mockResolvedValue({ ok: true } as Response)
  })

  it("saves the edited geometry and closes the edit session", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1", {
      type: "LineString",
      coordinates: [
        [0, 0],
        [1, 1],
      ],
    })

    await commitDraftEdit()

    expect(mocks.updateDraftGeometry).toHaveBeenCalledWith(
      "d1",
      '{"type":"LineString","coordinates":[[0,0],[1,1]]}',
    )
    expect(store.drafts[0].geometryGeoJson).toBe(
      '{"type":"LineString","coordinates":[[0,0],[1,1]]}',
    )
    expect(mocks.showToast).toHaveBeenCalledWith("map_geometry_saved", "success")
    expect(mocks.geoman.features.delete).toHaveBeenCalledWith("g1")
    expect(mocks.setActiveGeomanFeatureId).toHaveBeenCalledWith(null)
    expect(store.activeDraftEditId).toBeNull()
    expect(mocks.disableEditMode).toHaveBeenCalledOnce()
  })

  it("does nothing when no draft edit is active", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = null

    await commitDraftEdit()

    expect(mocks.updateDraftGeometry).not.toHaveBeenCalled()
  })

  it("closes the session when the active draft vanished", async () => {
    store.activeDraftEditId = "d1"

    await commitDraftEdit()

    expect(mocks.updateDraftGeometry).not.toHaveBeenCalled()
    expect(store.activeDraftEditId).toBeNull()
    expect(mocks.disableEditMode).toHaveBeenCalledOnce()
  })

  it("cancels when no edited geometry can be read", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry(null)

    await commitDraftEdit()

    expect(mocks.updateDraftGeometry).not.toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("map_geometry_save_failed", "error")
    expect(mocks.showToast).toHaveBeenCalledWith("map_edit_cancelled", "info")
    expect(store.activeDraftEditId).toBeNull()
  })

  it("cancels when the edited feature has no geometry", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1")

    await commitDraftEdit()

    expect(mocks.updateDraftGeometry).not.toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("map_geometry_save_failed", "error")
  })

  it("maps a 422 status to the rules-not-met toast and cancels", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1", {
      type: "LineString",
      coordinates: [
        [0, 0],
        [1, 1],
      ],
    })
    mocks.updateDraftGeometry.mockResolvedValue({ ok: false, status: 422 } as Response)

    await commitDraftEdit()

    expect(mocks.showToast).toHaveBeenCalledWith("map_draft_rules_not_met", "error")
    expect(mocks.showToast).toHaveBeenCalledWith("map_edit_cancelled", "info")
  })

  it("maps a 400 status to the invalid-geometry toast", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1", {
      type: "LineString",
      coordinates: [
        [0, 0],
        [1, 1],
      ],
    })
    mocks.updateDraftGeometry.mockResolvedValue({ ok: false, status: 400 } as Response)

    await commitDraftEdit()

    expect(mocks.showToast).toHaveBeenCalledWith("map_draft_invalid_geometry", "error")
  })

  it("maps a 409 status to the not-pending toast", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1", {
      type: "LineString",
      coordinates: [
        [0, 0],
        [1, 1],
      ],
    })
    mocks.updateDraftGeometry.mockResolvedValue({ ok: false, status: 409 } as Response)

    await commitDraftEdit()

    expect(mocks.showToast).toHaveBeenCalledWith("map_draft_not_pending", "error")
  })

  it("maps a 404 status to the not-found toast", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1", {
      type: "LineString",
      coordinates: [
        [0, 0],
        [1, 1],
      ],
    })
    mocks.updateDraftGeometry.mockResolvedValue({ ok: false, status: 404 } as Response)

    await commitDraftEdit()

    expect(mocks.showToast).toHaveBeenCalledWith("map_draft_not_found", "error")
  })

  it("falls back to the generic failure toast for other statuses", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1", {
      type: "LineString",
      coordinates: [
        [0, 0],
        [1, 1],
      ],
    })
    mocks.updateDraftGeometry.mockResolvedValue({ ok: false, status: 500 } as Response)

    await commitDraftEdit()

    expect(mocks.showToast).toHaveBeenCalledWith("map_geometry_save_failed", "error")
  })

  it("logs and reports failure when the save rejects", async () => {
    store.drafts = [draft("d1", "road", ROAD_GEOMETRY)]
    store.activeDraftEditId = "d1"
    editedGeometry("g1", {
      type: "LineString",
      coordinates: [
        [0, 0],
        [1, 1],
      ],
    })
    mocks.updateDraftGeometry.mockRejectedValue(new Error("boom"))

    await commitDraftEdit()

    expect(mocks.debugError).toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("map_save_failed", "error")
  })

  describe("polygon ring normalization", () => {
    function buildingEdit(ring: [number, number][]): void {
      store.drafts = [draft("d2", "building", BUILDING_GEOMETRY)]
      store.activeDraftEditId = "d2"
      editedGeometry("g2", { type: "LineString", coordinates: ring })
      mocks.updateDraftGeometry.mockResolvedValue({ ok: true } as Response)
    }

    it("keeps a closed ring as-is", async () => {
      buildingEdit([
        [0, 0],
        [1, 0],
        [1, 1],
        [0, 0],
      ])

      await commitDraftEdit()

      expect(mocks.updateDraftGeometry).toHaveBeenCalledWith(
        "d2",
        '{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}',
      )
    })

    it("closes an open ring", async () => {
      buildingEdit([
        [0, 0],
        [1, 0],
        [1, 1],
      ])

      await commitDraftEdit()

      expect(mocks.updateDraftGeometry).toHaveBeenCalledWith(
        "d2",
        '{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}',
      )
    })

    it("cancels when the reported ring is too short", async () => {
      buildingEdit([
        [0, 0],
        [1, 0],
      ])

      await commitDraftEdit()

      expect(mocks.updateDraftGeometry).not.toHaveBeenCalled()
      expect(mocks.showToast).toHaveBeenCalledWith("map_geometry_save_failed", "error")
    })
  })
})

describe("cancelDraftEdit", () => {
  let store: ReturnType<typeof useDraftsStore>

  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    store = useDraftsStore()
    mocks.geomanRef.current = mocks.geoman
  })

  it("removes the active geoman feature, exits edit mode and toasts", async () => {
    store.activeDraftEditId = "d1"
    useEditStore().setActiveGeomanFeatureId("g1")

    await cancelDraftEdit()

    expect(mocks.geoman.features.delete).toHaveBeenCalledWith("g1")
    expect(mocks.setActiveGeomanFeatureId).toHaveBeenCalledWith(null)
    expect(store.activeDraftEditId).toBeNull()
    expect(mocks.disableEditMode).toHaveBeenCalledOnce()
    expect(mocks.showToast).toHaveBeenCalledWith("map_edit_cancelled", "info")
  })

  it("tolerates a missing geoman feature id", async () => {
    store.activeDraftEditId = "d1"

    await cancelDraftEdit()

    expect(mocks.geoman.features.delete).not.toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("map_edit_cancelled", "info")
  })
})
