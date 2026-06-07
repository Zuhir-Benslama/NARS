import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { mockApiFetch, createMockSuccessResponse } from "../../test/setup"
import EntranceInspectionForm from "./EntranceInspectionForm.vue"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../../lib/toast", () => ({
  showToast: vi.fn(),
}))

describe("EntranceInspectionForm", () => {
  beforeEach(() => {
    mockApiFetch.mockReset()
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
  })

  function mountForm(
    feature: { id: string; label: string } | null = { id: "f1", label: "Main Entrance" },
  ) {
    return mount(EntranceInspectionForm, { props: { feature } as any })
  }

  describe("rendering", () => {
    it("renders the feature label", () => {
      const wrapper = mountForm()
      expect(wrapper.text()).toContain("Main Entrance")
    })

    it("shows 'Unknown entrance' when feature is null", () => {
      const wrapper = mountForm(null)
      expect(wrapper.text()).toContain("Unknown entrance")
    })

    it("shows step 1 on mount", () => {
      const wrapper = mountForm()
      expect(wrapper.text()).toContain("Does the entrance exist?")
    })
  })

  describe("step progression", () => {
    it("step1: Yes → step2 (numbering panel)", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      expect(wrapper.text()).toContain("Does it have a numbering panel?")
    })

    it("step1: No → no_entrance result", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-no").trigger("click")
      expect(wrapper.text()).toContain("Entrance is missing")
    })

    it("no_entrance: Add Entrance button shown", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-no").trigger("click")
      expect(wrapper.find(".eif-btn-primary").exists()).toBe(true)
      expect(wrapper.find(".eif-btn-primary").text()).toContain("Add Entrance")
    })

    it("step2: Yes → step3 (number correct)", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      expect(wrapper.text()).toContain("Is the number correct?")
    })

    it("step2: No → no_panel result", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-no").trigger("click")
      expect(wrapper.text()).toContain("Numbering panel is missing")
    })

    it("step3: Yes → step4 (position correct)", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      expect(wrapper.text()).toContain("Is the numbering panel position correct?")
    })

    it("step3: No → wrong_number result", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-no").trigger("click")
      expect(wrapper.text()).toContain("Number is incorrect")
    })

    it("step4: Yes → good result (all passed)", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      expect(wrapper.text()).toContain("All checks passed")
    })

    it("step4: No → wrong_position result", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-no").trigger("click")
      expect(wrapper.text()).toContain("Numbering panel position is incorrect")
    })
  })

  describe("submission", () => {
    it("submits inspection with status 'good'", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-yes").trigger("click")
      await wrapper.find(".eif-btn-submit").trigger("click")

      expect(mockApiFetch).toHaveBeenCalledWith(
        "/api/field/inspect",
        expect.objectContaining({
          method: "POST",
          body: expect.stringContaining('"status":"good"'),
        }),
      )
    })

    it("submits inspection with status 'issue'", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-no").trigger("click")
      await wrapper.find(".eif-btn-submit").trigger("click")

      expect(mockApiFetch).toHaveBeenCalledWith(
        "/api/field/inspect",
        expect.objectContaining({
          method: "POST",
          body: expect.stringContaining('"status":"issue"'),
        }),
      )
    })

    it("shows loading state while submitting", async () => {
      mockApiFetch.mockImplementationOnce(() => new Promise(() => {})) // never resolves
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-no").trigger("click")
      await wrapper.find(".eif-btn-submit").trigger("click")
      expect(wrapper.find(".eif-loading").exists()).toBe(true)
    })

    it("emits 'done' on successful submission", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-no").trigger("click")
      await wrapper.find(".eif-btn-submit").trigger("click")
      await new Promise((resolve) => setTimeout(resolve, 0))
      expect(wrapper.emitted("done")).toBeTruthy()
    })
  })

  describe("create entrance", () => {
    it("calls create entrance API then submits issue", async () => {
      const wrapper = mountForm()
      await wrapper.find(".eif-btn-no").trigger("click")
      await wrapper.find(".eif-btn-primary").trigger("click")
      await new Promise((resolve) => setTimeout(resolve, 0))

      expect(mockApiFetch).toHaveBeenCalledWith(
        "/api/field/entrance/create",
        expect.objectContaining({
          method: "POST",
        }),
      )
    })
  })
})
