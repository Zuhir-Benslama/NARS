import { describe, it, expect, beforeEach, vi } from "vitest"
import { mount } from "@vue/test-utils"
import { setActivePinia, createPinia } from "pinia"

import GenerationProgress from "./GenerationProgress.vue"
import { useGenerationStore } from "../stores/generationStore"

vi.mock("../i18n", () => ({ t: (key: string) => key }))

const globalOpts = {
  stubs: {
    Teleport: { template: "<div><slot /></div>" },
    Transition: { template: "<div><slot /></div>" },
  },
}

describe("GenerationProgress", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("renders nothing while idle", () => {
    const wrapper = mount(GenerationProgress, { global: globalOpts })
    expect(wrapper.find("#nars-generation-progress").exists()).toBe(false)
  })

  it("renders the bar with stage label and percentage when active", () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_detect")
    store.setProgress(35)

    const wrapper = mount(GenerationProgress, { global: globalOpts })
    const el = wrapper.find("#nars-generation-progress")
    expect(el.exists()).toBe(true)
    expect(el.attributes("role")).toBe("progressbar")
    expect(el.attributes("aria-valuenow")).toBe("35")
    expect(wrapper.text()).toContain("gen_roads_stage_detect")
    expect(wrapper.text()).toContain("35%")
  })

  it("sizes the fill bar to the progress percentage", () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    store.setProgress(70)

    const wrapper = mount(GenerationProgress, { global: globalOpts })
    expect(wrapper.find(".generation-bar-fill").attributes("style")).toContain("70%")
  })

  it("disappears when the store goes idle", async () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    const wrapper = mount(GenerationProgress, { global: globalOpts })
    expect(wrapper.find("#nars-generation-progress").exists()).toBe(true)

    store.abort()
    await wrapper.vm.$nextTick()
    expect(wrapper.find("#nars-generation-progress").exists()).toBe(false)
  })

  it("resets the store on unmount", () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    const wrapper = mount(GenerationProgress, { global: globalOpts })
    wrapper.unmount()
    expect(store.active).toBe(false)
  })
})
