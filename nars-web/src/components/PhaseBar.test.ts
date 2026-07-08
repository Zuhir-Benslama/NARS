import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { setActivePinia, createPinia } from "pinia"

const mockGoToPhase = vi.hoisted(() => vi.fn())

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

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
}))

vi.mock("../map", () => ({
  goToPhase: mockGoToPhase,
}))

import PhaseBar from "./PhaseBar.vue"
import { useAppStore } from "../stores/appStore"

describe("PhaseBar", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it("renders all phase steps", () => {
    const wrapper = mount(PhaseBar)
    const steps = wrapper.findAll(".phase-step")
    expect(steps).toHaveLength(7)
  })

  it("marks first step as active when currentPhase is 0", () => {
    const wrapper = mount(PhaseBar)
    const steps = wrapper.findAll(".phase-step")
    expect(steps[0].classes()).toContain("active")
    expect(steps[1].classes()).toContain("locked")
  })

  it("marks earlier steps as done when currentPhase > 0", () => {
    const store = useAppStore()
    store.currentPhase = 3
    const wrapper = mount(PhaseBar)
    const steps = wrapper.findAll(".phase-step")
    expect(steps[0].classes()).toContain("done")
    expect(steps[1].classes()).toContain("done")
    expect(steps[2].classes()).toContain("done")
    expect(steps[3].classes()).toContain("active")
    expect(steps[4].classes()).toContain("locked")
  })

  it("shows checkmark badge on done steps", () => {
    const store = useAppStore()
    store.currentPhase = 2
    const wrapper = mount(PhaseBar)
    const badges = wrapper.findAll(".phase-badge")
    expect(badges[0].text()).toBe("✓")
    expect(badges[1].text()).toBe("✓")
    expect(badges[2].text()).toBe("3")
  })

  it("calls goToPhase on click", async () => {
    const wrapper = mount(PhaseBar)
    const steps = wrapper.findAll(".phase-step")
    await steps[2].trigger("click")
    expect(mockGoToPhase).toHaveBeenCalledWith(2)
  })

  it("renders connectors between steps", () => {
    const wrapper = mount(PhaseBar)
    const connectors = wrapper.findAll(".phase-connector")
    expect(connectors).toHaveLength(6)
  })
})
