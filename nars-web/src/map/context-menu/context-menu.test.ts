import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

const {
  mockIsSnappingEnabled,
  mockToggleSnapping,
  mockShowToast,
  mockSetHouseNumbers,
  mockSetReferenceRoad,
  mockClearReferenceRoad,
  mockSetReferenceEntrance,
  mockGenerateNamingPanels,
  mockComputeAndApplyRoadDirections,
  mockGenerateRoadsFromUrbanAreas,
  mockGenerateDistrictsFromUrbanAreas,
  mockUpdateEndpointMarkersExport,
  mockEnableEditGeometry,
  mockEditFeatureInfo,
  mockRemoveFeature,
  mockRemoveAllRoads,
  mockFindLayerEntryByDbId,
  mockT,
} = vi.hoisted(() => ({
  mockIsSnappingEnabled: vi.fn(),
  mockToggleSnapping: vi.fn(),
  mockShowToast: vi.fn(),
  mockSetHouseNumbers: vi.fn(),
  mockSetReferenceRoad: vi.fn(),
  mockClearReferenceRoad: vi.fn(),
  mockSetReferenceEntrance: vi.fn(),
  mockGenerateNamingPanels: vi.fn(),
  mockComputeAndApplyRoadDirections: vi.fn(),
  mockGenerateRoadsFromUrbanAreas: vi.fn(() => Promise.resolve({ created: 0, dropped: 0 })),
  mockGenerateDistrictsFromUrbanAreas: vi.fn(() =>
    Promise.resolve({ created: 0, absorbedSlivers: 0 }),
  ),
  mockUpdateEndpointMarkersExport: vi.fn(),
  mockEnableEditGeometry: vi.fn(),
  mockEditFeatureInfo: vi.fn(),
  mockRemoveFeature: vi.fn(),
  mockRemoveAllRoads: vi.fn(),
  mockFindLayerEntryByDbId: vi.fn(),
  mockT: vi.fn((key: string) => key),
}))

vi.mock("../draw/draw-complete", () => ({ isSnappingEnabled: mockIsSnappingEnabled }))
vi.mock("../snapping/snapping", () => ({ toggleSnapping: mockToggleSnapping }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast }))
vi.mock("../house-numbering", () => ({ setHouseNumbers: mockSetHouseNumbers }))
vi.mock("../house-entrances", () => ({
  setReferenceRoad: mockSetReferenceRoad,
  clearReferenceRoad: mockClearReferenceRoad,
  setReferenceEntrance: mockSetReferenceEntrance,
}))
vi.mock("../naming-panels", () => ({ generateNamingPanels: mockGenerateNamingPanels }))
vi.mock("../roads/road-directions", () => ({
  computeAndApplyRoadDirections: mockComputeAndApplyRoadDirections,
  updateEndpointMarkers: mockUpdateEndpointMarkersExport,
}))
vi.mock("../generate/generate-roads", () => ({
  generateRoadsFromUrbanAreas: mockGenerateRoadsFromUrbanAreas,
}))
vi.mock("../generate/generate-districts", () => ({
  generateDistrictsFromUrbanAreas: mockGenerateDistrictsFromUrbanAreas,
}))
vi.mock("./ctx-menu-actions", () => ({
  enableEditGeometry: mockEnableEditGeometry,
  editFeatureInfo: mockEditFeatureInfo,
  removeFeature: mockRemoveFeature,
  removeAllRoads: mockRemoveAllRoads,
  findLayerEntryByDbId: mockFindLayerEntryByDbId,
}))
vi.mock("../../i18n", () => ({ t: mockT }))

let mod: any
let useAppStore: any
let useContextMenuStore: any

function setupPhase(key: string) {
  useAppStore().currentPhase = PHASES.findIndex((p: any) => p.key === key)
}

const PHASES = [
  { key: "areas", label: "Areas" },
  { key: "roads", label: "Roads" },
  { key: "cityCenter", label: "City Center" },
  { key: "districts", label: "Districts" },
  { key: "houseEntrances", label: "House Entrances" },
  { key: "publicBuildings", label: "Public Buildings" },
  { key: "publicSpaces", label: "Public Spaces" },
  { key: "namingPanels", label: "Naming Panels" },
]

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())

  mod = await import("./context-menu")

  const as = await import("../../stores/appStore")
  useAppStore = as.useAppStore
  const cs = await import("../../stores/contextMenuStore")
  useContextMenuStore = cs.useContextMenuStore
})

describe("per-phase edit/remove rule", () => {
  // Regression: canEdit was `(isCurrentPhase || isArea) && ...`, so areas were
  // editable AND removable from every phase — the urban area could be deleted
  // while standing in the naming-panels phase, contradicting
  // alert_areas_uneditable_in_districts.
  it("does not allow editing or removing an area outside the areas phase", () => {
    setupPhase("namingPanels")

    mod.showContextMenu(0, 0, "a1", "areas")

    const store = useContextMenuStore()
    for (const label of ["ctx_edit_geom", "ctx_edit_info", "ctx_remove"]) {
      const item = store.items.find((i: any) => i.label === label)
      expect(item, label).toBeDefined()
      expect(item?.disabled, label).toBe(true)
      // Inert: no handler is attached, so nothing can mutate the feature.
      expect(item?.onClick, label).toBeUndefined()
    }
    expect(mockEnableEditGeometry).not.toHaveBeenCalled()
    expect(mockEditFeatureInfo).not.toHaveBeenCalled()
    expect(mockRemoveFeature).not.toHaveBeenCalled()
  })

  it("does not allow editing or removing a district outside the districts phase", () => {
    setupPhase("roads")

    mod.showContextMenu(0, 0, "d1", "districts")

    const store = useContextMenuStore()
    expect(store.items.find((i: any) => i.label === "ctx_edit_info")?.disabled).toBe(true)
    expect(store.items.find((i: any) => i.label === "ctx_remove")?.disabled).toBe(true)
  })

  it("allows editing and removing inside the feature's own phase", () => {
    setupPhase("districts")

    mod.showContextMenu(0, 0, "d1", "districts")

    const store = useContextMenuStore()
    for (const label of ["ctx_edit_geom", "ctx_edit_info", "ctx_remove"]) {
      const item = store.items.find((i: any) => i.label === label)
      expect(item?.disabled, label).toBeFalsy()
      expect(typeof item?.onClick, label).toBe("function")
    }
  })

  it("explains the rule on the disabled items", () => {
    setupPhase("roads")

    mod.showContextMenu(0, 0, "a1", "areas")

    const store = useContextMenuStore()
    const info = store.items.find((i: any) => i.label === "ctx_edit_info")
    const remove = store.items.find((i: any) => i.label === "ctx_remove")
    expect(info?.disabledReason).toBe("alert_switch_phase_to_edit")
    expect(remove?.disabledReason).toBe("alert_switch_phase_to_remove")
  })

  it("blocks roads in the house entrances phase", () => {
    setupPhase("houseEntrances")

    mod.showContextMenu(0, 0, "r1", "roads")

    const store = useContextMenuStore()
    const info = store.items.find((i: any) => i.label === "ctx_edit_info")
    expect(info?.disabled).toBe(true)
    expect(info?.onClick).toBeUndefined()
  })

  it("offers no edit/remove path for house entrances at all", () => {
    setupPhase("houseEntrances")

    mod.showContextMenu(0, 0, "h1", "houseEntrances")

    const store = useContextMenuStore()
    const labels = store.items.map((i: any) => i.label)
    expect(labels).not.toContain("ctx_edit_info")
    expect(labels).not.toContain("ctx_remove")
  })
})

describe("showContextMenu / bindContextMenu", () => {
  it("showContextMenu opens context menu store", () => {
    setupPhase("areas")

    mod.showContextMenu(100, 200, "db42", "areas")

    const store = useContextMenuStore()
    expect(store.visible).toBe(true)
    expect(store.x).toBe(100)
    expect(store.y).toBe(200)
  })

  it("bindContextMenu passes event coordinates", () => {
    setupPhase("areas")

    const e = { originalEvent: { clientX: 50, clientY: 60 }, point: { x: 10, y: 20 } }
    mod.bindContextMenu(e, "db42", "areas")

    const store = useContextMenuStore()
    expect(store.x).toBe(50)
    expect(store.y).toBe(60)
  })

  it("bindContextMenu falls back to point when no originalEvent", () => {
    setupPhase("areas")

    const e = { point: { x: 11, y: 22 } }
    mod.bindContextMenu(e, "db42", "areas")

    const store = useContextMenuStore()
    expect(store.x).toBe(11)
    expect(store.y).toBe(22)
  })

  it("includes edit/remove items for editable features", () => {
    setupPhase("areas")

    mod.showContextMenu(0, 0, "a1", "areas")

    const store = useContextMenuStore()
    const labels = store.items.map((i: any) => i.label)
    expect(labels).toContain("ctx_edit_geom")
    expect(labels).toContain("ctx_edit_info")
    expect(labels).toContain("ctx_remove")
  })

  it("does not include edit items for houseEntrances features", () => {
    setupPhase("houseEntrances")

    mod.showContextMenu(0, 0, "he1", "houseEntrances")

    const store = useContextMenuStore()
    expect(store.items.some((i: any) => i.label === "ctx_edit_geom")).toBe(false)
  })

  it("includes road direction item for roads in roads phase", () => {
    setupPhase("roads")

    mod.showContextMenu(0, 0, "r1", "roads")

    const store = useContextMenuStore()
    const dirItem = store.items.find((i: any) => i.label === "ctx_road_dir")
    expect(dirItem).toBeDefined()
    dirItem.onClick()
    expect(mockComputeAndApplyRoadDirections).toHaveBeenCalled()
  })

  it("includes generate-roads item for any feature hit in the roads phase", () => {
    setupPhase("roads")

    mod.showContextMenu(0, 0, "a1", "areas")

    const store = useContextMenuStore()
    const genItem = store.items.find((i: any) => i.label === "ctx_generate_roads")
    expect(genItem).toBeDefined()
    genItem.onClick()
    expect(mockGenerateRoadsFromUrbanAreas).toHaveBeenCalled()
  })

  it("does not include generate-roads item outside the roads phase", () => {
    setupPhase("houseEntrances")

    mod.showContextMenu(0, 0, "r1", "roads")

    const store = useContextMenuStore()
    expect(store.items.some((i: any) => i.label === "ctx_generate_roads")).toBe(false)
  })

  it("offers generate-districts (not generate-roads) in the districts phase", () => {
    setupPhase("districts")

    mod.showContextMenu(0, 0, "d1", "districts")

    const store = useContextMenuStore()
    expect(store.items.some((i: any) => i.label === "ctx_generate_roads")).toBe(false)
    const genItem = store.items.find((i: any) => i.label === "ctx_generate_districts")
    expect(genItem).toBeDefined()
    genItem.onClick()
    expect(mockGenerateDistrictsFromUrbanAreas).toHaveBeenCalled()
    // Road segmentation must not be triggered from the districts phase.
    expect(mockGenerateRoadsFromUrbanAreas).not.toHaveBeenCalled()
  })

  it("offers generate-districts for any feature hit in the districts phase", () => {
    setupPhase("districts")

    mod.showContextMenu(0, 0, "r1", "roads")

    const genItem = useContextMenuStore().items.find(
      (i: any) => i.label === "ctx_generate_districts",
    )
    expect(genItem).toBeDefined()
  })

  it("never offers the combined roads+districts item", () => {
    for (const key of ["roads", "districts"]) {
      setupPhase(key)
      mod.showContextMenu(0, 0, "a1", "areas")
      const labels = useContextMenuStore().items.map((i: any) => i.label)
      expect(labels).not.toContain("ctx_generate_roads_and_districts")
    }
  })

  it("includes remove-all-roads item in the roads phase", () => {
    setupPhase("roads")

    mod.showContextMenu(0, 0, "r1", "roads")

    const store = useContextMenuStore()
    const item = store.items.find((i: any) => i.label === "ctx_remove_all_roads")
    expect(item).toBeDefined()
    expect(item.danger).toBe(true)
    item.onClick()
    expect(mockRemoveAllRoads).toHaveBeenCalled()
  })

  it("does not include remove-all-roads item outside the roads phase", () => {
    setupPhase("houseEntrances")

    mod.showContextMenu(0, 0, "r1", "roads")

    const store = useContextMenuStore()
    expect(store.items.some((i: any) => i.label === "ctx_remove_all_roads")).toBe(false)
  })

  it("shows cityCenter lock when not on cityCenter phase", () => {
    setupPhase("areas")

    mod.showContextMenu(0, 0, "cc42", "cityCenter")

    const store = useContextMenuStore()
    const lockItem = store.items.find((i: any) => i.label === "ctx_cc_lock")
    expect(lockItem).toBeDefined()
    lockItem.onClick()
    expect(mockShowToast).toHaveBeenCalledWith("ctx_cc_lock_msg", "info")
  })

  it("includes set reference road for road in houseEntrances phase", () => {
    setupPhase("houseEntrances")
    useAppStore().referenceRoadDbId = "other"

    mod.showContextMenu(0, 0, "r1", "roads")

    const store = useContextMenuStore()
    const refItem = store.items.find((i: any) => i.label === "ctx_road_ref")
    expect(refItem).toBeDefined()
    refItem.onClick()
    expect(mockSetReferenceRoad).toHaveBeenCalledWith("r1")
  })

  it("includes clear reference road for current ref", () => {
    setupPhase("houseEntrances")
    useAppStore().referenceRoadDbId = "r1"

    mod.showContextMenu(0, 0, "r1", "roads")

    const store = useContextMenuStore()
    const clearItem = store.items.find((i: any) => i.label === "ctx_road_ref_remove")
    expect(clearItem).toBeDefined()
    clearItem.onClick()
    expect(mockClearReferenceRoad).toHaveBeenCalled()
  })

  it("always includes snap toggle as last item", () => {
    setupPhase("areas")

    mod.showContextMenu(0, 0, "a1", "areas")

    const store = useContextMenuStore()
    const lastItem = store.items[store.items.length - 1]
    expect(lastItem.label).toContain("snapping")
  })
})

describe("showMapContextMenu", () => {
  function mkPhase(key: string) {
    return { key, label: key, color: "#000" }
  }

  it("adds computeAndApplyRoadDirections for roads phase", async () => {
    await mod.showMapContextMenu(0, 0, mkPhase("roads"))

    const store = useContextMenuStore()
    expect(store.visible).toBe(true)
    const roadDirItem = store.items.find((i: any) => i.label === "ctx_road_dir")
    expect(roadDirItem).toBeDefined()
    roadDirItem.onClick()
    expect(mockComputeAndApplyRoadDirections).toHaveBeenCalled()
  })

  it("adds generate-roads item for roads phase", async () => {
    await mod.showMapContextMenu(0, 0, mkPhase("roads"))

    const store = useContextMenuStore()
    const genItem = store.items.find((i: any) => i.label === "ctx_generate_roads")
    expect(genItem).toBeDefined()
    genItem.onClick()
    expect(mockGenerateRoadsFromUrbanAreas).toHaveBeenCalled()
  })

  it("adds generate-districts item for districts phase", async () => {
    await mod.showMapContextMenu(0, 0, mkPhase("districts"))

    const store = useContextMenuStore()
    expect(store.items.some((i: any) => i.label === "ctx_generate_roads")).toBe(false)
    const genItem = store.items.find((i: any) => i.label === "ctx_generate_districts")
    expect(genItem).toBeDefined()
    genItem.onClick()
    expect(mockGenerateDistrictsFromUrbanAreas).toHaveBeenCalled()
  })

  it("does not add generate-districts item outside the districts phase", async () => {
    await mod.showMapContextMenu(0, 0, mkPhase("roads"))

    const labels = useContextMenuStore().items.map((i: any) => i.label)
    expect(labels).not.toContain("ctx_generate_districts")
  })

  it("adds remove-all-roads item for roads phase", async () => {
    await mod.showMapContextMenu(0, 0, mkPhase("roads"))

    const store = useContextMenuStore()
    const item = store.items.find((i: any) => i.label === "ctx_remove_all_roads")
    expect(item).toBeDefined()
    expect(item.danger).toBe(true)
    item.onClick()
    expect(mockRemoveAllRoads).toHaveBeenCalled()
  })

  it("adds setHouseNumbers for houseEntrances phase", async () => {
    await mod.showMapContextMenu(0, 0, mkPhase("houseEntrances"))

    const store = useContextMenuStore()
    const item = store.items.find((i: any) => i.label === "ctx_house_nums")
    expect(item).toBeDefined()
    item.onClick()
    expect(mockSetHouseNumbers).toHaveBeenCalled()
  })

  it("adds generateNamingPanels for namingPanels phase", async () => {
    await mod.showMapContextMenu(0, 0, mkPhase("namingPanels"))

    const store = useContextMenuStore()
    const item = store.items.find((i: any) => i.label === "ctx_set_naming_panels")
    expect(item).toBeDefined()
    item.onClick()
    expect(mockGenerateNamingPanels).toHaveBeenCalled()
  })

  it("only shows snap toggle for areas phase", async () => {
    mockIsSnappingEnabled.mockReturnValue(false)

    await mod.showMapContextMenu(0, 0, mkPhase("areas"))

    const store = useContextMenuStore()
    expect(store.items.length).toBe(1)
    expect(store.items[0].label).toContain("snapping")
  })

  it("always includes snap toggle", async () => {
    mockIsSnappingEnabled.mockReturnValue(false)

    await mod.showMapContextMenu(0, 0, mkPhase("areas"))

    const store = useContextMenuStore()
    const snapItem = store.items.find((i: any) => i.label && i.label.includes("snapping"))
    expect(snapItem).toBeDefined()
  })
})

describe("re-exports", () => {
  it("re-exports context menu action functions", () => {
    expect(mod.enableEditGeometry).toBe(mockEnableEditGeometry)
    expect(mod.editFeatureInfo).toBe(mockEditFeatureInfo)
    expect(mod.removeFeature).toBe(mockRemoveFeature)
    expect(mod.removeAllRoads).toBe(mockRemoveAllRoads)
    expect(mod.findLayerEntryByDbId).toBe(mockFindLayerEntryByDbId)
    expect(mod.computeAndApplyRoadDirections).toBe(mockComputeAndApplyRoadDirections)
    expect(mod.updateEndpointMarkers).toBe(mockUpdateEndpointMarkersExport)
  })
})
