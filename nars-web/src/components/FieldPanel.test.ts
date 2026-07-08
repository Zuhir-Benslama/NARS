import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"
import { setActivePinia, createPinia } from "pinia"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import FieldPanel from "./FieldPanel.vue"
import { useFieldStore } from "../stores/fieldStore"

const globalOpts = {
  stubs: {
    RoadInspectionForm: { template: '<div class="mock-road-form">Road</div>' },
    EntranceInspectionForm: { template: '<div class="mock-entrance-form">Entrance</div>' },
    NamingPanelInspectionForm: { template: '<div class="mock-panel-form">Panel</div>' },
  },
}

describe("FieldPanel", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("renders header and tabs", () => {
    const wrapper = mount(FieldPanel, { global: globalOpts })
    expect(wrapper.text()).toContain("Field Inspection")
    const tabs = wrapper.findAll(".fp-tab")
    expect(tabs).toHaveLength(3)
    expect(tabs[0].text()).toBe("Roads")
    expect(tabs[1].text()).toBe("Entrances")
    expect(tabs[2].text()).toBe("Naming Panels")
  })

  it("starts with roads tab active", () => {
    const wrapper = mount(FieldPanel, { global: globalOpts })
    const tabs = wrapper.findAll(".fp-tab")
    expect(tabs[0].classes()).toContain("active")
  })

  it("switches active tab on click", async () => {
    const wrapper = mount(FieldPanel, { global: globalOpts })
    const tabs = wrapper.findAll(".fp-tab")
    await tabs[1].trigger("click")
    expect(tabs[1].classes()).toContain("active")
    expect(tabs[0].classes()).not.toContain("active")
  })

  it("shows loading state", async () => {
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: () => new Promise<never>(() => {}),
      },
    })
    await nextTick()
    expect(wrapper.find(".fp-loading").exists()).toBe(true)
  })

  it("shows empty state when no features", async () => {
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => [],
      },
    })
    await nextTick()
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.find(".fp-empty").exists()).toBe(true)
  })

  it("renders feature list from fetch", async () => {
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => [
          { id: "1", label: "Main Road" },
          { id: "2", label: "Secondary Road" },
        ],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    const items = wrapper.findAll(".fp-item")
    expect(items).toHaveLength(2)
    expect(items[0].text()).toContain("Main Road")
  })

  it("selects a feature and shows inspection form", async () => {
    const store = useFieldStore()
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => [{ id: "1", label: "Road 1" }],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.find(".fp-item").trigger("click")
    expect(store.selectedFeature).toEqual({ id: "1", label: "Road 1", type: "road" })
    expect(wrapper.find(".mock-road-form").exists()).toBe(true)
  })

  it("shows entrance form for entrance feature", async () => {
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => [{ id: "2", label: "Entrance 1" }],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    const tabs = wrapper.findAll(".fp-tab")
    await tabs[1].trigger("click")
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.find(".fp-item").trigger("click")
    expect(wrapper.find(".mock-entrance-form").exists()).toBe(true)
  })

  it("shows panel form for naming_panel feature", async () => {
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => [{ id: "3", label: "Panel 1" }],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    const tabs = wrapper.findAll(".fp-tab")
    await tabs[2].trigger("click")
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.find(".fp-item").trigger("click")
    expect(wrapper.find(".mock-panel-form").exists()).toBe(true)
  })

  it("shows back button after selection", async () => {
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => [{ id: "1", label: "Road 1" }],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.find(".fp-item").trigger("click")
    expect(wrapper.find(".fp-back").exists()).toBe(true)
  })

  it("clears selection on back button", async () => {
    const store = useFieldStore()
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => [{ id: "1", label: "Road 1" }],
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.find(".fp-item").trigger("click")
    await wrapper.find(".fp-back").trigger("click")
    expect(store.selectedFeature).toBeNull()
    expect(wrapper.find(".fp-list").exists()).toBe(true)
  })

  it("handles fetch error gracefully", async () => {
    const wrapper = mount(FieldPanel, {
      global: globalOpts,
      props: {
        fetchFeaturesFn: async () => {
          throw new Error("Network error")
        },
      },
    })
    await new Promise((r) => setTimeout(r, 0))
    expect(wrapper.find(".fp-empty").exists()).toBe(true)
  })
})
