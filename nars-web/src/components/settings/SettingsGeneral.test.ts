import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"

const mockSetLang = vi.hoisted(() => vi.fn())

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../../i18n", async () => {
  const { ref } = await import("vue")
  return {
    setLang: mockSetLang,
    currentLang: ref("en"),
  }
})

import SettingsGeneral from "./SettingsGeneral.vue"
import { theme } from "../../composables/useTheme"

describe("SettingsGeneral", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    theme.value = "dark"
  })

  it("renders language and theme options", () => {
    const wrapper = mount(SettingsGeneral)
    expect(wrapper.text()).toContain("label_language")
    expect(wrapper.text()).toContain("label_theme")
    expect(wrapper.findAll("option").map((o) => o.attributes("value"))).toEqual(["en", "fr", "ar"])
  })

  it("marks the current theme as selected", () => {
    theme.value = "dark"
    const wrapper = mount(SettingsGeneral)
    const darkBtn = wrapper.findAll(".theme-btn")[1]
    const lightBtn = wrapper.findAll(".theme-btn")[0]
    expect(darkBtn.classes()).toContain("selected")
    expect(lightBtn.classes()).not.toContain("selected")
  })

  it("switches to light theme on click", async () => {
    const wrapper = mount(SettingsGeneral)
    await wrapper.findAll(".theme-btn")[0].trigger("click")
    await nextTick()
    expect(theme.value).toBe("light")
    expect(wrapper.findAll(".theme-btn")[0].classes()).toContain("selected")
  })

  it("switches back to dark theme on click", async () => {
    theme.value = "light"
    const wrapper = mount(SettingsGeneral)
    await wrapper.findAll(".theme-btn")[1].trigger("click")
    await nextTick()
    expect(theme.value).toBe("dark")
  })

  it("calls setLang when the language select changes", async () => {
    const wrapper = mount(SettingsGeneral)
    const select = wrapper.find("select")
    await select.setValue("fr")
    await nextTick()
    expect(mockSetLang).toHaveBeenCalledWith("fr")
  })
})
