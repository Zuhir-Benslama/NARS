import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { setActivePinia, createPinia } from "pinia"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

const mockCommitEditMode = vi.hoisted(() => vi.fn())

vi.mock("../map/edit/edit-mode", () => ({
  commitEditMode: mockCommitEditMode,
}))

import EditSaveButton from "./EditSaveButton.vue"
import { useEditStore } from "../stores/editStore"

describe("EditSaveButton", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it("is hidden when isEditMode is false", () => {
    const wrapper = mount(EditSaveButton)
    expect(wrapper.find("#nars-edit-save").exists()).toBe(false)
  })

  it("is visible when isEditMode is true", () => {
    const store = useEditStore()
    store.isEditMode = true
    const wrapper = mount(EditSaveButton)
    expect(wrapper.find("#nars-edit-save").exists()).toBe(true)
  })

  it("renders Save Geometry text", () => {
    const store = useEditStore()
    store.isEditMode = true
    const wrapper = mount(EditSaveButton)
    expect(wrapper.text()).toContain("btn_save_geometry")
  })

  it("calls commitEditMode on click", async () => {
    mockCommitEditMode.mockResolvedValueOnce(undefined)
    const store = useEditStore()
    store.isEditMode = true
    const wrapper = mount(EditSaveButton)
    await wrapper.find("#nars-edit-save").trigger("click")
    expect(mockCommitEditMode).toHaveBeenCalledOnce()
  })

  it("handles commitEditMode rejection gracefully", async () => {
    mockCommitEditMode.mockRejectedValueOnce(new Error("fail"))
    const store = useEditStore()
    store.isEditMode = true
    const wrapper = mount(EditSaveButton)
    await wrapper.find("#nars-edit-save").trigger("click")
    expect(mockCommitEditMode).toHaveBeenCalledOnce()
  })
})
