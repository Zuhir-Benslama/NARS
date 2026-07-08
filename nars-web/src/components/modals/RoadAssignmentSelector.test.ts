import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { setActivePinia, createPinia } from "pinia"

interface ModalState {
  entranceTypeKey: string
  isEdit: boolean
  selectedRoadIdx: number | ""
  roadOptions: { idx: number; label: string }[]
  entranceNumber: number | null
  entranceSide: string | null
  entranceSideLoading: boolean
  selectedMainIdx: number | ""
  mainEntranceOptions: { idx: number; label: string }[]
  bisNumber: number | null
  errors: Record<string, string>
}

const modalState = vi.hoisted(
  (): ModalState => ({
    entranceTypeKey: "main_entrance",
    isEdit: false,
    selectedRoadIdx: "",
    roadOptions: [
      { idx: 1, label: "Street A" },
      { idx: 2, label: "Avenue B" },
    ],
    entranceNumber: null,
    entranceSide: null,
    entranceSideLoading: false,
    selectedMainIdx: "",
    mainEntranceOptions: [{ idx: 10, label: "Entrance X" }],
    bisNumber: null,
    errors: {},
  }),
)

vi.mock("../../stores/modalStore", () => ({
  useModalStore: () => modalState,
}))

import RoadAssignmentSelector from "./RoadAssignmentSelector.vue"

describe("RoadAssignmentSelector", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    modalState.entranceTypeKey = "main_entrance"
    modalState.isEdit = false
    modalState.selectedRoadIdx = ""
    modalState.entranceSide = null
    modalState.selectedMainIdx = ""
  })

  it("renders road select for main entrance", () => {
    const wrapper = mount(RoadAssignmentSelector)
    expect(wrapper.text()).toContain("Assign to Road")
    expect(wrapper.find("select").exists()).toBe(true)
  })

  it("renders road options", () => {
    const wrapper = mount(RoadAssignmentSelector)
    const options = wrapper.findAll("option")
    expect(options).toHaveLength(3)
    expect(options[1].text()).toBe("Street A")
    expect(options[2].text()).toBe("Avenue B")
  })

  it("shows entrance number field after road selection", async () => {
    modalState.selectedRoadIdx = 1
    const wrapper = mount(RoadAssignmentSelector)
    expect(wrapper.text()).toContain("Entrance Number")
  })

  it("shows side text when entrance side is set", () => {
    modalState.entranceSide = "left"
    modalState.selectedRoadIdx = 1
    const wrapper = mount(RoadAssignmentSelector)
    expect(wrapper.text()).toContain("Left side")
  })

  it("shows right side text for right entrance", () => {
    modalState.entranceSide = "right"
    modalState.selectedRoadIdx = 1
    const wrapper = mount(RoadAssignmentSelector)
    expect(wrapper.text()).toContain("Right side")
  })

  it("shows main entrance select for secondary entrance", () => {
    modalState.entranceTypeKey = "secondary_entrance"
    const wrapper = mount(RoadAssignmentSelector)
    expect(wrapper.text()).toContain("Assign to Main Entrance")
  })

  it("renders main entrance options", () => {
    modalState.entranceTypeKey = "secondary_entrance"
    const wrapper = mount(RoadAssignmentSelector)
    const options = wrapper.findAll("option")
    expect(options).toHaveLength(2)
    expect(options[1].text()).toBe("Entrance X")
  })

  it("shows BIS number when set", () => {
    modalState.entranceTypeKey = "secondary_entrance"
    modalState.bisNumber = 3
    const wrapper = mount(RoadAssignmentSelector)
    const bisInput = wrapper.find('input[type="text"]')
    expect(bisInput.exists()).toBe(true)
    expect((bisInput.element as HTMLInputElement).value).toBe("BIS03")
  })

  it("hides road select in edit mode", () => {
    modalState.isEdit = true
    const wrapper = mount(RoadAssignmentSelector)
    expect(wrapper.text()).not.toContain("Assign to Road")
  })
})
