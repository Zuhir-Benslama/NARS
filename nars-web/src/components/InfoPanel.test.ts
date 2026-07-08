import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { setActivePinia, createPinia } from "pinia"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import InfoPanel from "./InfoPanel.vue"
import { useAppStore } from "../stores/appStore"

describe("InfoPanel", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("renders info title", () => {
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("info_title")
  })

  it("displays counts from store", () => {
    const store = useAppStore()
    store.counts = {
      areas: 5,
      districts: 3,
      roads: 12,
      mainEntrances: 20,
      secondaryEntrances: 8,
      publicBuildings: 2,
      publicSpaces: 4,
      cityCenter: 1,
      namingPanels: 0,
    }
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("5")
    expect(wrapper.text()).toContain("3")
    expect(wrapper.text()).toContain("12")
    expect(wrapper.text()).toContain("20")
    expect(wrapper.text()).toContain("8")
    expect(wrapper.text()).toContain("2")
    expect(wrapper.text()).toContain("4")
  })

  it('shows "placed" for city center when count > 0', () => {
    const store = useAppStore()
    store.counts.cityCenter = 1
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("info_status_placed")
  })

  it('shows "skipped" for city center when cityCenterMode is auto', () => {
    const store = useAppStore()
    store.counts.cityCenter = 0
    store.cityCenterMode = "auto"
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("info_status_skipped")
  })

  it("shows em dash for city center when not placed and not auto", () => {
    const store = useAppStore()
    store.counts.cityCenter = 0
    store.cityCenterMode = null
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("—")
  })

  it("shows zero counts by default", () => {
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("0")
  })
})
