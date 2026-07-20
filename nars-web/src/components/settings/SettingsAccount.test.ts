import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"
import { setActivePinia, createPinia } from "pinia"

const mockApiFetch = vi.hoisted(() => vi.fn())
const mockShowToast = vi.hoisted(() => vi.fn())

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../../api", () => ({
  apiFetch: mockApiFetch,
}))

vi.mock("../../lib/toast", () => ({
  showToast: mockShowToast,
}))

import SettingsAccount from "./SettingsAccount.vue"
import { useAppStore } from "../../stores/appStore"

describe("SettingsAccount", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it("renders form fields", () => {
    const wrapper = mount(SettingsAccount, { props: { visible: false } })
    expect(wrapper.text()).toContain("label_username")
    expect(wrapper.text()).toContain("label_email")
    expect(wrapper.text()).toContain("label_password")
  })

  it("pre-fills username and email from store", () => {
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "jdoe",
      email: "john@test.com",
      name: "John",
      commune: { name_fr: "X", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    const wrapper = mount(SettingsAccount, { props: { visible: false } })
    const inputs = wrapper.findAll("input")
    expect((inputs[0].element as HTMLInputElement).value).toBe("jdoe")
    expect((inputs[1].element as HTMLInputElement).value).toBe("john@test.com")
  })

  it("syncs form when visible becomes true", async () => {
    const store = useAppStore()
    const wrapper = mount(SettingsAccount, { props: { visible: false } })
    store.user = {
      id: 1,
      username: "sync_user",
      email: "sync@test.com",
      name: "Sync",
      commune: { name_fr: "X", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    await wrapper.setProps({ visible: true })
    const inputs = wrapper.findAll("input")
    expect((inputs[0].element as HTMLInputElement).value).toBe("sync_user")
  })

  it("shows error for empty username on save", async () => {
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    await wrapper.find(".modal-btn-save").trigger("click")
    expect(mockShowToast).toHaveBeenCalledWith("error_username_required", "error")
  })

  it("shows error for short username on save", async () => {
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("ab")
    await inputs[1].setValue("a@b.com")
    await wrapper.find(".modal-btn-save").trigger("click")
    expect(mockShowToast).toHaveBeenCalledWith("error_username_min_length", "error")
  })

  it("shows error for invalid email on save", async () => {
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("validuser")
    await inputs[1].setValue("not-an-email")
    await wrapper.find(".modal-btn-save").trigger("click")
    expect(mockShowToast).toHaveBeenCalledWith("error_email_invalid", "error")
  })

  it("calls API on valid form submission", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "jdoe",
      email: "john@test.com",
      name: "John",
      commune: { name_fr: "X", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("newuser")
    await inputs[1].setValue("new@email.com")
    await inputs[2].setValue("newpass123")
    await wrapper.find(".modal-btn-save").trigger("click")
    expect(mockApiFetch).toHaveBeenCalledWith(
      "/api/user/update",
      expect.objectContaining({
        method: "PUT",
        body: expect.stringContaining("newuser"),
      }),
    )
  })

  it("shows success toast on API success", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("validuser")
    await inputs[1].setValue("valid@email.com")
    await wrapper.find(".modal-btn-save").trigger("click")
    await nextTick()
    expect(mockShowToast).toHaveBeenCalledWith("alert_account_updated", "success")
  })

  it("shows error toast on API failure", async () => {
    mockApiFetch.mockResolvedValueOnce({
      ok: false,
      json: vi.fn().mockResolvedValue({ detail: "Invalid credentials" }),
    })
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("validuser")
    await inputs[1].setValue("valid@email.com")
    await wrapper.find(".modal-btn-save").trigger("click")
    await nextTick()
    expect(mockShowToast).toHaveBeenCalledWith("Invalid credentials", "error")
  })

  it("clears password field after successful update", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("validuser")
    await inputs[1].setValue("valid@email.com")
    await inputs[2].setValue("tempPass123")
    await wrapper.find(".modal-btn-save").trigger("click")
    await nextTick()
    expect((inputs[2].element as HTMLInputElement).value).toBe("")
  })

  it("requires password >= 8 chars", async () => {
    const wrapper = mount(SettingsAccount, { props: { visible: true } })
    const inputs = wrapper.findAll("input")
    await inputs[0].setValue("validuser")
    await inputs[1].setValue("valid@email.com")
    await inputs[2].setValue("short")
    await wrapper.find(".modal-btn-save").trigger("click")
    expect(mockShowToast).toHaveBeenCalledWith("error_password_min_length", "error")
  })
})
