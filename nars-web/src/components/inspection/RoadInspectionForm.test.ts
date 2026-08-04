import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key}:${JSON.stringify(params)}` : key,
  }),
}))

const mockApiFetch = vi.hoisted(() => vi.fn())
const mockShowToast = vi.hoisted(() => vi.fn())
const mockGetUserMessageKey = vi.hoisted(() => vi.fn(() => "err_unknown"))

vi.mock("../../api", () => ({
  apiFetch: mockApiFetch,
}))

vi.mock("../../lib/toast", () => ({
  showToast: mockShowToast,
}))

vi.mock("../../lib/errors", () => ({
  getUserMessageKey: mockGetUserMessageKey,
}))

import RoadInspectionForm from "./RoadInspectionForm.vue"

describe("RoadInspectionForm", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("renders title and feature label", () => {
    const wrapper = mount(RoadInspectionForm, {
      props: { feature: { id: "1", label: "Main Road" } },
    })
    expect(wrapper.text()).toContain("rif_title")
    expect(wrapper.text()).toContain("Main Road")
  })

  it("renders traffic options with medium active by default", () => {
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    const btns = wrapper.findAll(".rif-btn")
    expect(btns[1].classes()).toContain("active")
  })

  it("switches traffic option on click", async () => {
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    const btns = wrapper.findAll(".rif-btn")
    await btns[0].trigger("click")
    expect(btns[0].classes()).toContain("active")
    expect(btns[1].classes()).not.toContain("active")
  })

  it("updates numLanes on input", async () => {
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    const input = wrapper.find('input[type="number"]')
    await input.setValue(4)
    expect((input.element as HTMLInputElement).value).toBe("4")
  })

  it("toggles checkbox fields", async () => {
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    const checkboxes = wrapper.findAll('input[type="checkbox"]')
    expect(checkboxes).toHaveLength(4)
    await checkboxes[0].setValue(true)
    expect((checkboxes[0].element as HTMLInputElement).checked).toBe(true)
  })

  it("submit button is enabled by default", () => {
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    expect(wrapper.find(".rif-submit").attributes("disabled")).toBeUndefined()
  })

  it("calls API on submit with good status", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    await wrapper.find(".rif-submit").trigger("click")
    expect(mockApiFetch).toHaveBeenCalledWith(
      "/api/field/inspect",
      expect.objectContaining({
        method: "POST",
        body: expect.stringContaining("road"),
      }),
    )
  })

  it("emits done on successful submit", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    await wrapper.find(".rif-submit").trigger("click")
    await nextTick()
    expect(wrapper.emitted("done")).toBeTruthy()
  })

  it("shows success toast on submit", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    await wrapper.find(".rif-submit").trigger("click")
    await nextTick()
    expect(mockShowToast).toHaveBeenCalledWith("alert_road_inspection_saved", "success")
  })

  it("shows error toast when API fails", async () => {
    mockApiFetch.mockRejectedValueOnce(new Error("Server error"))
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    await wrapper.find(".rif-submit").trigger("click")
    await nextTick()
    expect(mockShowToast).toHaveBeenCalledWith("err_unknown", "error")
  })

  it("shows network error toast on exception", async () => {
    mockApiFetch.mockRejectedValueOnce(new Error("Network failed"))
    mockGetUserMessageKey.mockReturnValue("err_network")
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    await wrapper.find(".rif-submit").trigger("click")
    await nextTick()
    expect(mockShowToast).toHaveBeenCalledWith("err_network", "error")
  })

  it("disables submit button while submitting", async () => {
    mockApiFetch.mockImplementationOnce(() => new Promise(() => {}))
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    await wrapper.find(".rif-submit").trigger("click")
    expect(wrapper.find(".rif-submit").attributes("disabled")).toBeDefined()
  })

  it("detects issues when numLanes is 0", async () => {
    const wrapper = mount(RoadInspectionForm, { props: { feature: { id: "1", label: "R" } } })
    const input = wrapper.find('input[type="number"]')
    await input.setValue(0)
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    await wrapper.find(".rif-submit").trigger("click")
    await nextTick()
    expect(mockShowToast).toHaveBeenCalledWith("alert_issues_reported", "error")
  })
})
