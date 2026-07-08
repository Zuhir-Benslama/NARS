import { describe, it, expect, vi } from "vitest"
import { mount } from "@vue/test-utils"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import SettingsAbout from "./SettingsAbout.vue"

describe("SettingsAbout", () => {
  it("renders about content", () => {
    const wrapper = mount(SettingsAbout)
    expect(wrapper.text()).toContain("about_nars")
    expect(wrapper.text()).toContain("about_version")
    expect(wrapper.text()).toContain("about_body")
    expect(wrapper.text()).toContain("about_copyright")
  })

  it("has about-panel class", () => {
    const wrapper = mount(SettingsAbout)
    expect(wrapper.find(".about-panel").exists()).toBe(true)
  })

  it("renders version element", () => {
    const wrapper = mount(SettingsAbout)
    expect(wrapper.find(".version").exists()).toBe(true)
  })
})
