import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { mockApiFetch, createMockSuccessResponse } from "../../test/setup"

const mockShowToast = vi.hoisted(() => vi.fn())

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../../lib/toast", () => ({
  showToast: mockShowToast,
}))

import SettingsFeatures from "./SettingsFeatures.vue"

describe("SettingsFeatures", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockApiFetch.mockReset()
  })

  it("renders the form fields", () => {
    const wrapper = mount(SettingsFeatures)
    expect(wrapper.text()).toContain("hint_features")
    expect(wrapper.text()).toContain("label_category")
    expect(wrapper.text()).toContain("label_feature_label")
    expect(wrapper.find("button.modal-btn-save").exists()).toBe(true)
  })

  it("does not submit an empty label", async () => {
    const wrapper = mount(SettingsFeatures)
    await wrapper.find("button.modal-btn-save").trigger("click")
    expect(mockApiFetch).not.toHaveBeenCalled()
  })

  it("posts the category and label on add", async () => {
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
    const wrapper = mount(SettingsFeatures)
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("Zone verte")
    await wrapper.find("select").setValue("publicSpaces")
    await wrapper.find("button.modal-btn-save").trigger("click")
    expect(mockApiFetch).toHaveBeenCalledWith(
      "/api/feature-types/custom",
      expect.objectContaining({
        method: "POST",
        body: expect.stringMatching(/publicSpaces.*Zone verte/),
      }),
    )
  })

  it("shows a success toast and clears the label", async () => {
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
    const wrapper = mount(SettingsFeatures)
    const input = wrapper.find("input")
    await input.setValue("Zone verte")
    await wrapper.find("button.modal-btn-save").trigger("click")
    expect(mockShowToast).toHaveBeenCalledWith("alert_feature_added", "success")
    expect((input.element as HTMLInputElement).value).toBe("")
  })

  it("shows an error toast when the API rejects", async () => {
    mockApiFetch.mockResolvedValue({ ok: false, status: 500 })
    const wrapper = mount(SettingsFeatures)
    const input = wrapper.find("input")
    await input.setValue("Zone verte")
    await wrapper.find("button.modal-btn-save").trigger("click")
    expect(mockShowToast).toHaveBeenCalledWith("error_add_feature_failed", "error")
  })

  it("shows an error toast when the request throws", async () => {
    mockApiFetch.mockRejectedValue(new Error("network"))
    const wrapper = mount(SettingsFeatures)
    const input = wrapper.find("input")
    await input.setValue("Zone verte")
    await wrapper.find("button.modal-btn-save").trigger("click")
    expect(mockShowToast).toHaveBeenCalledWith("error_add_feature_failed", "error")
  })
})
