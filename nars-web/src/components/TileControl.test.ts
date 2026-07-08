import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"

const mockSetBaseLayer = vi.hoisted(() => vi.fn())

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../map/index", () => ({
  setBaseLayer: mockSetBaseLayer,
}))

import TileControl from "./TileControl.vue"
import { vClickOutside } from "../directives/clickOutside"

const globalOpts = {
  directives: { "click-outside": vClickOutside },
}

describe("TileControl", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("renders toggle button", () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    expect(wrapper.find(".tile-toggle").exists()).toBe(true)
  })

  it("dropdown is hidden by default", () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    expect(wrapper.find(".tile-dropdown").exists()).toBe(false)
  })

  it("opens dropdown on toggle click", async () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    await wrapper.find(".tile-toggle").trigger("click")
    expect(wrapper.find(".tile-dropdown").exists()).toBe(true)
  })

  it("closes dropdown on second toggle click", async () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    await wrapper.find(".tile-toggle").trigger("click")
    await wrapper.find(".tile-toggle").trigger("click")
    expect(wrapper.find(".tile-dropdown").exists()).toBe(false)
  })

  it("renders all four layer options", async () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    await wrapper.find(".tile-toggle").trigger("click")
    const items = wrapper.findAll(".tile-item")
    expect(items).toHaveLength(4)
  })

  it("calls setBaseLayer on layer selection", async () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    await wrapper.find(".tile-toggle").trigger("click")
    await wrapper.findAll(".tile-item")[1].trigger("click")
    expect(mockSetBaseLayer).toHaveBeenCalledWith("street")
  })

  it("closes dropdown on layer selection", async () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    await wrapper.find(".tile-toggle").trigger("click")
    await wrapper.findAll(".tile-item")[0].trigger("click")
    expect(wrapper.find(".tile-dropdown").exists()).toBe(false)
  })

  it("does not call setBaseLayer when selecting active layer", async () => {
    const wrapper = mount(TileControl, { global: globalOpts })
    await wrapper.find(".tile-toggle").trigger("click")
    await wrapper.findAll(".tile-item")[0].trigger("click")
    expect(mockSetBaseLayer).not.toHaveBeenCalled()
  })
})
