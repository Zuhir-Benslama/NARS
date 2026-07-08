import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"

import { setActivePinia, createPinia } from "pinia"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import SettingsModal from "./SettingsModal.vue"
import { useAppStore } from "../stores/appStore"

const globalOpts = {
  stubs: {
    SettingsGeneral: { template: '<div class="mock-general">General</div>' },
    SettingsAccount: { template: '<div class="mock-account">Account</div>' },
    SettingsUsers: { template: '<div class="mock-users">Users</div>' },
    SettingsFeatures: { template: '<div class="mock-features">Features</div>' },
    SettingsAbout: { template: '<div class="mock-about">About</div>' },
  },
}

describe("SettingsModal", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("is hidden when visible is false", () => {
    const wrapper = mount(SettingsModal, { props: { visible: false }, global: globalOpts })
    expect(wrapper.find(".modal").exists()).toBe(false)
  })

  it("shows when visible is true", () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    expect(wrapper.find(".modal").exists()).toBe(true)
  })

  it("renders the settings title", () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    expect(wrapper.text()).toContain("settings_title")
  })

  it("shows default tabs (general, account, features, about)", () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    const tabs = wrapper.findAll(".sidebar-tab")
    expect(tabs).toHaveLength(4)
    expect(tabs[0].text()).toContain("tab_general")
    expect(tabs[1].text()).toContain("tab_account")
    expect(tabs[2].text()).toContain("tab_features")
    expect(tabs[3].text()).toContain("tab_about")
  })

  it("shows users tab for admin users", () => {
    const store = useAppStore()
    store.user = {
      id: 1,
      username: "admin",
      name: "Admin",
      email: "admin@test.com",
      commune: { name_fr: "X", name_ar: "", id: 1, latitude: null, longitude: null },
      role: "national_admin",
    }
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    const tabs = wrapper.findAll(".sidebar-tab")
    expect(tabs).toHaveLength(5)
    expect(tabs[2].text()).toContain("tab_users")
  })

  it("starts with general tab active", () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    const generalTab = wrapper.findAll(".sidebar-tab")[0]
    expect(generalTab.classes()).toContain("active")
  })

  it("switches tab on click", async () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    const tabs = wrapper.findAll(".sidebar-tab")
    await tabs[1].trigger("click")
    expect(tabs[1].classes()).toContain("active")
    expect(tabs[0].classes()).not.toContain("active")
  })

  it("renders correct panel for active tab", async () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    expect(wrapper.find(".mock-general").exists()).toBe(true)
    const tabs = wrapper.findAll(".sidebar-tab")
    await tabs[1].trigger("click")
    expect(wrapper.find(".mock-account").exists()).toBe(true)
  })

  it("navigates tabs with arrow right", async () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    const sidebar = wrapper.find(".settings-sidebar")
    await sidebar.trigger("keydown", { key: "ArrowRight" })
    const tabs = wrapper.findAll(".sidebar-tab")
    expect(tabs[1].classes()).toContain("active")
  })

  it("navigates tabs with arrow left", async () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    const sidebar = wrapper.find(".settings-sidebar")
    await sidebar.trigger("keydown", { key: "ArrowRight" })
    await sidebar.trigger("keydown", { key: "ArrowLeft" })
    const tabs = wrapper.findAll(".sidebar-tab")
    expect(tabs[0].classes()).toContain("active")
  })

  it("emits close event on close button", async () => {
    const wrapper = mount(SettingsModal, { props: { visible: true }, global: globalOpts })
    await wrapper.find(".modal-btn-cancel").trigger("click")
    expect(wrapper.emitted("close")).toBeTruthy()
  })
})
