import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"

const mockApiFetch = vi.hoisted(() => vi.fn())
const mockShowToast = vi.hoisted(() => vi.fn())
const mockGetErrorMessage = vi.hoisted(() => vi.fn((e) => String(e)))

vi.mock("../../api", () => ({
  apiFetch: mockApiFetch,
}))

vi.mock("../../lib/toast", () => ({
  showToast: mockShowToast,
}))

vi.mock("../../lib/errors", () => ({
  getErrorMessage: mockGetErrorMessage,
}))

import NamingPanelInspectionForm from "./NamingPanelInspectionForm.vue"

describe("NamingPanelInspectionForm", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("renders title and feature label", () => {
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "Panel A" } },
    })
    expect(wrapper.text()).toContain("Naming Panel Inspection")
    expect(wrapper.text()).toContain("Panel A")
  })

  it("starts at step 1", () => {
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    expect(wrapper.text()).toContain("Is the naming panel location present?")
  })

  it("moves to step 2 on yes for location", async () => {
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    await wrapper.find(".npf-btn-yes").trigger("click")
    expect(wrapper.text()).toContain("Is the naming panel present?")
  })

  it("shows no_location result on no for location", async () => {
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    await wrapper.find(".npf-btn-no").trigger("click")
    expect(wrapper.text()).toContain("Naming panel location is missing")
  })

  it("moves through all steps to good", async () => {
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    await wrapper.find(".npf-btn-yes").trigger("click")
    await wrapper.find(".npf-btn-yes").trigger("click")
    await wrapper.find(".npf-btn-yes").trigger("click")
    await wrapper.find(".npf-btn-yes").trigger("click")
    expect(wrapper.text()).toContain("All checks passed")
  })

  it("reaches wrong_naming on no for naming", async () => {
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    await wrapper.find(".npf-btn-yes").trigger("click")
    await wrapper.find(".npf-btn-yes").trigger("click")
    await wrapper.find(".npf-btn-no").trigger("click")
    expect(wrapper.text()).toContain("Naming on the panel is incorrect")
  })

  it("calls API on submit", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    await wrapper.find(".npf-btn-no").trigger("click")
    await wrapper.find(".npf-btn-submit").trigger("click")
    expect(mockApiFetch).toHaveBeenCalledWith("/api/field/inspect", expect.any(Object))
  })

  it("emits done on successful submit", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    await wrapper.find(".npf-btn-no").trigger("click")
    await wrapper.find(".npf-btn-submit").trigger("click")
    await nextTick()
    expect(wrapper.emitted("done")).toBeTruthy()
  })

  it("shows loading state during submit", async () => {
    mockApiFetch.mockImplementationOnce(() => new Promise(() => {}))
    const wrapper = mount(NamingPanelInspectionForm, {
      props: { feature: { id: "1", label: "P" } },
    })
    await wrapper.find(".npf-btn-no").trigger("click")
    await wrapper.find(".npf-btn-submit").trigger("click")
    expect(wrapper.text()).toContain("Saving")
  })
})
