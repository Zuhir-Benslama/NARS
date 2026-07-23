import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"
import { setActivePinia, createPinia } from "pinia"

const mockApiFetch = vi.hoisted(() => vi.fn())
const mockShowToast = vi.hoisted(() => vi.fn())
const mockGetLoginPath = vi.hoisted(() => vi.fn(() => "/login"))

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../api", () => ({
  apiFetch: mockApiFetch,
}))

vi.mock("../config", async (importOriginal) => {
  const mod = await importOriginal<typeof import("../config")>()
  return { ...mod, getLoginPath: mockGetLoginPath }
})

vi.mock("../lib/toast", () => ({
  showToast: mockShowToast,
}))

import ProfileMenu from "./ProfileMenu.vue"
import { useAppStore } from "../stores/appStore"
import { vClickOutside } from "../directives/clickOutside"

const globalOpts = {
  directives: { "click-outside": vClickOutside },
  stubs: {
    SettingsModal: {
      template: '<div v-if="$attrs.visible" class="mock-settings-modal">Settings</div>',
    },
  },
}

describe("ProfileMenu", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
  })

  it("renders profile button with user data", () => {
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "jdoe",
      name: "John Doe",
      email: "j@t.com",
      commune: { name_fr: "Test", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    expect(wrapper.text()).toContain("jdoe")
    expect(wrapper.text()).toContain("John Doe")
  })

  it("shows loading text when user is null", () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    expect(wrapper.text()).toContain("loading")
  })

  it("shows initials from username", () => {
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "jdoe",
      name: "John",
      email: "j@t.com",
      commune: { name_fr: "Test", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    expect(wrapper.find(".profile-icon").text()).toBe("J")
  })

  it("dropdown is hidden by default", () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    expect(wrapper.find(".profile-dropdown").classes()).not.toContain("show")
  })

  it("opens dropdown on click", async () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("click")
    expect(wrapper.find(".profile-dropdown").classes()).toContain("show")
  })

  it("closes dropdown on second click", async () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("click")
    await wrapper.find(".profile-button").trigger("click")
    expect(wrapper.find(".profile-dropdown").classes()).not.toContain("show")
  })

  it("opens dropdown on Enter key", async () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("keydown.enter")
    expect(wrapper.find(".profile-dropdown").classes()).toContain("show")
  })

  it("opens dropdown on Space key", async () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("keydown.space")
    expect(wrapper.find(".profile-dropdown").classes()).toContain("show")
  })

  it("shows settings modal on settings click", async () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("click")
    await wrapper.find(".dropdown-item").trigger("click")
    expect(wrapper.find(".mock-settings-modal").exists()).toBe(true)
  })

  it("calls logout API on logout click", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "jdoe",
      name: "John",
      email: "j@t.com",
      commune: { name_fr: "Test", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("click")
    await wrapper.findAll(".dropdown-item")[1].trigger("click")
    expect(mockApiFetch).toHaveBeenCalledWith("/api/logout", expect.any(Object))
  })

  it("redirects on successful logout", async () => {
    mockApiFetch.mockResolvedValueOnce({ ok: true })
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "jdoe",
      name: "John",
      email: "j@t.com",
      commune: { name_fr: "Test", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    Object.defineProperty(window, "location", { value: { href: "" }, writable: true })
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("click")
    await wrapper.findAll(".dropdown-item")[1].trigger("click")
    await vi.waitFor(() => {
      expect(window.location.href).toBe("/login")
    })
  })

  it("shows toast on failed logout", async () => {
    mockApiFetch.mockRejectedValueOnce(new Error("Network error"))
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "jdoe",
      name: "John",
      email: "j@t.com",
      commune: { name_fr: "Test", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "field_worker",
    }
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("click")
    await wrapper.findAll(".dropdown-item")[1].trigger("click")
    await vi.waitFor(() => {
      expect(mockShowToast).toHaveBeenCalledWith("alert_logout_failed", "error")
    })
  })

  it("navigates dropdown with arrow keys", async () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts, attachTo: document.body })
    await wrapper.find(".profile-button").trigger("click")
    const dropdownEl = wrapper.find(".profile-dropdown").element
    dropdownEl.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }))
    const items = document.querySelectorAll<HTMLElement>(".dropdown-item")
    expect(items.length).toBe(2)
    wrapper.unmount()
  })

  it("closes dropdown on Escape", async () => {
    const wrapper = mount(ProfileMenu, { global: globalOpts })
    await wrapper.find(".profile-button").trigger("click")
    wrapper.find(".profile-dropdown").trigger("keydown", { key: "Escape" })
    await nextTick()
    expect(wrapper.find(".profile-dropdown").classes()).not.toContain("show")
  })
})
