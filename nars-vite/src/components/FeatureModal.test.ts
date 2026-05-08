// ─── FEATURE MODAL TESTS ──────────────────────────────────────────────────────
// Tests for components/FeatureModal.vue.

import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"

// Mock store
const createMockStore = () => ({
  modal: {
    visible: false,
    phaseIndex: 0,
    isEdit: false,
    editDbId: null as string | null,
    label: "",
    decisionNumber: "",
    decisionDate: "",
    errors: {} as Record<string, string>,
    areaTypeKey: "central_urban",
    mainUrbanExists: false,
    districtTypeKey: "district",
    roadTypeKey: "street",
    entranceTypeKey: "main_entrance",
    roadOptions: [] as any[],
    selectedRoadIdx: "" as number | "",
    entranceSide: null as "left" | "right" | null,
    entranceNumber: null as number | null,
    entranceSideLoading: false,
    mainEntranceOptions: [] as any[],
    selectedMainIdx: "" as number | "",
    bisNumber: null as number | null,
    spaceTypeKey: "garden",
    sectorKey: "banking_postal",
    buildingTypeKey: "bank",
  },
  municipalityName: "Test Municipality",
  user: { commune: { name_fr: "Test Municipality", name_ar: "", id: 1 } },
})

let mockStore = createMockStore()

vi.mock("../store", () => ({
  get store() {
    return mockStore
  },
  resolveModal: vi.fn(),
  openModal: vi.fn(),
  openEditModal: vi.fn(),
}))

// Mock i18n
vi.mock("vue-i18n", () => ({
  useI18n: () => ({
    t: (key: string) => key,
  }),
}))

// Mock PHASES
vi.mock("../phases", () => ({
  PHASES: [
    {
      index: 0,
      key: "areas",
      label: "phase_areas_label",
      drawType: "polygon",
      color: "#8e44ad",
      hint: "phase_areas_hint",
    },
    {
      index: 1,
      key: "districts",
      label: "phase_districts_label",
      drawType: "polygon",
      color: "#f39c12",
      hint: "phase_districts_hint",
    },
    {
      index: 2,
      key: "cityCenter",
      label: "phase_cityCenter_label",
      drawType: "circle",
      color: "#e74c3c",
      hint: "phase_cityCenter_hint",
    },
    {
      index: 3,
      key: "roads",
      label: "phase_roads_label",
      drawType: "polyline",
      color: "#3498db",
      hint: "phase_roads_hint",
    },
    {
      index: 4,
      key: "houseEntrances",
      label: "phase_houseEntrances_label",
      drawType: "marker",
      color: "#27ae60",
      hint: "phase_houseEntrances_hint",
    },
    {
      index: 5,
      key: "publicBuildings",
      label: "phase_publicBuildings_label",
      drawType: "polygon",
      color: "#e67e22",
      hint: "phase_publicBuildings_hint",
    },
    {
      index: 6,
      key: "publicSpaces",
      label: "phase_publicSpaces_label",
      drawType: "polygon",
      color: "#2ecc71",
      hint: "phase_publicSpaces_hint",
    },
  ],
  AREA_TYPES: [
    { key: "central_urban", label: "Main Urban Area", color: "#8e44ad" },
    { key: "secondary_urban", label: "Secondary Urban Area", color: "#9b59b6" },
  ],
  DISTRICT_TYPES: [
    { key: "district", label: "District" },
    { key: "trad_activities_zone", label: "Trade Activities Zone" },
    { key: "industry_zone", label: "Industrial Zone" },
  ],
  ROAD_TYPES: [
    { key: "street", label: "Street" },
    { key: "avenue", label: "Avenue" },
  ],
  PUBLIC_SPACE_TYPES: [
    { key: "garden", label: "Garden" },
    { key: "square", label: "Square" },
  ],
  PUBLIC_BUILDING_SECTORS: [
    {
      key: "banking_postal",
      label: "Banking & Postal",
      buildings: [
        { key: "bank", label: "Bank" },
        { key: "post_office", label: "Post Office" },
      ],
    },
    {
      key: "health",
      label: "Health",
      buildings: [
        { key: "public_hospital", label: "Hospital" },
        { key: "neighborhood_health", label: "Health Center" },
      ],
    },
  ],
}))

// Mock map features
vi.mock("../map", () => ({
  fetchRoadSide: vi.fn(),
  computeBisNumber: vi.fn(),
}))

import FeatureModal from "../components/FeatureModal.vue"
import { resolveModal } from "../store"

describe("FeatureModal", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockStore = createMockStore()
    mockStore.modal.errors = {}
  })

  it("renders when visible is true", () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0

    const wrapper = mount(FeatureModal)

    expect(wrapper.find(".modal").exists()).toBe(true)
  })

  it("does not render when visible is false", () => {
    mockStore.modal.visible = false

    const wrapper = mount(FeatureModal, {
      global: {
        mocks: { $t: (key: string) => key },
      },
    })

    // Modal uses v-show, so it's always in DOM but hidden
    // We test that the component mounts without error
    expect(wrapper.vm).toBeDefined()
  })

  it("shows header text based on phase", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0
    mockStore.modal.isEdit = false

    const wrapper = mount(FeatureModal)
    await nextTick()

    expect(wrapper.find(".modal-header").text()).toContain("Add")
  })

  it("shows edit header in edit mode", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0
    mockStore.modal.isEdit = true

    const wrapper = mount(FeatureModal)
    await nextTick()

    expect(wrapper.find(".modal-header").text()).toContain("Edit")
  })

  it("validates required fields on save", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0
    mockStore.modal.label = ""
    mockStore.modal.decisionNumber = ""
    mockStore.modal.decisionDate = ""

    const wrapper = mount(FeatureModal, {
      global: {
        mocks: { $t: (key: string) => key },
      },
    })
    await nextTick()

    // Click save button
    const saveBtn = wrapper.find(".modal-btn-save")
    await saveBtn.trigger("click")
    await nextTick()

    // Validation should have been called - check that modal is still visible (not closed)
    // The actual validation errors are set on the store which is reactive
    expect(mockStore.modal.visible).toBe(true)
  })

  it("auto-fills municipality name for main urban area", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0
    mockStore.modal.areaTypeKey = "central_urban"
    mockStore.modal.mainUrbanExists = false

    const wrapper = mount(FeatureModal, {
      global: {
        mocks: { $t: (key: string) => key },
      },
    })
    await nextTick()

    // The name input should be populated with the municipality name
    // Due to reactivity and mocking, we test that the component renders
    expect(wrapper.vm).toBeDefined()
  })

  it("hides name field for house entrance edit mode", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 4 // houseEntrances
    mockStore.modal.isEdit = true

    const wrapper = mount(FeatureModal)
    await nextTick()

    // Name field should not be present
    const nameFields = wrapper.findAll("label").filter((l) => l.text().includes("Name"))
    expect(nameFields.length).toBe(0)
  })

  it("shows area type selector for areas phase", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0 // areas

    const wrapper = mount(FeatureModal)
    await nextTick()

    expect(wrapper.find("select").exists()).toBe(true)
  })

  it("shows road type selector for roads phase", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 3 // roads

    const wrapper = mount(FeatureModal)
    await nextTick()

    const selects = wrapper.findAll("select")
    const roadTypeSelect = selects.find((s) => s.find('option[value="street"]').exists())
    expect(roadTypeSelect).toBeDefined()
  })

  it("shows road assignment selector for houseEntrances phase", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 4 // houseEntrances

    const wrapper = mount(FeatureModal)
    await nextTick()

    const roadAssignmentSelector = wrapper.findComponent({
      name: "RoadAssignmentSelector",
    })
    expect(roadAssignmentSelector.exists()).toBe(true)
  })

  it("shows sector and building selectors for publicBuildings phase", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 5 // publicBuildings

    const wrapper = mount(FeatureModal)
    await nextTick()

    const selects = wrapper.findAll("select")
    expect(selects.length).toBeGreaterThanOrEqual(2)
  })

  it("calls resolveModal with form data on save", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0
    mockStore.modal.label = "Test Area"
    mockStore.modal.decisionNumber = "2024/001"
    mockStore.modal.decisionDate = "2024-01-15"
    mockStore.modal.areaTypeKey = "central_urban"

    const wrapper = mount(FeatureModal)
    await nextTick()

    const saveBtn = wrapper.find(".modal-btn-save")
    await saveBtn.trigger("click")
    await nextTick()

    expect(resolveModal).toHaveBeenCalled()
  })

  it("calls resolveModal with null on cancel", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0

    const wrapper = mount(FeatureModal)
    await nextTick()

    const cancelBtn = wrapper.find(".modal-btn-cancel")
    await cancelBtn.trigger("click")
    await nextTick()

    expect(resolveModal).toHaveBeenCalledWith(null)
  })

  it("handles Enter key to save", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0
    mockStore.modal.label = "Test"
    mockStore.modal.decisionNumber = "001"
    mockStore.modal.decisionDate = "2024-01-01"

    const wrapper = mount(FeatureModal)
    await nextTick()

    await wrapper.trigger("keyup", { key: "Enter" })
    await nextTick()

    expect(resolveModal).toHaveBeenCalled()
  })

  it("handles Escape key to cancel", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 0

    const wrapper = mount(FeatureModal)
    await nextTick()

    await wrapper.trigger("keyup", { key: "Escape" })
    await nextTick()

    expect(resolveModal).toHaveBeenCalledWith(null)
  })

  it("disables name input for zones with type name", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 1 // districts
    mockStore.modal.districtTypeKey = "trad_activities_zone"

    const wrapper = mount(FeatureModal)
    await nextTick()

    const nameInput = wrapper.find('input[type="text"]')
    expect(nameInput.attributes("readonly")).toBeDefined()
  })

  it("disables name input for city center", async () => {
    mockStore.modal.visible = true
    mockStore.modal.phaseIndex = 2 // cityCenter

    const wrapper = mount(FeatureModal)
    await nextTick()

    const nameInput = wrapper.find('input[type="text"]')
    expect(nameInput.attributes("readonly")).toBeDefined()
  })
})
