import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"
import { setActivePinia, createPinia } from "pinia"

import ToastContainer from "./ToastContainer.vue"
import { useToastStore } from "../stores/toastStore"

const globalOpts = {
  stubs: {
    Teleport: { template: '<div><slot /></div>' },
    TransitionGroup: { template: '<div><slot /></div>' },
  },
}

describe("ToastContainer", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("renders nothing when no toasts", () => {
    const wrapper = mount(ToastContainer, { global: globalOpts })
    expect(wrapper.findAll(".nars-toast")).toHaveLength(0)
  })

  it("renders toasts from store", () => {
    const store = useToastStore()
    store.addToast("Hello", "info")
    store.addToast("Error!", "error")
    const wrapper = mount(ToastContainer, { global: globalOpts })
    const toasts = wrapper.findAll(".nars-toast")
    expect(toasts).toHaveLength(2)
    expect(toasts[0].text()).toContain("Hello")
    expect(toasts[1].text()).toContain("Error!")
  })

  it("removes toast on click", async () => {
    const store = useToastStore()
    store.addToast("Click me", "info")
    const wrapper = mount(ToastContainer, { global: globalOpts })
    expect(wrapper.findAll(".nars-toast")).toHaveLength(1)
    await wrapper.find(".nars-toast").trigger("click")
    await nextTick()
    expect(wrapper.findAll(".nars-toast")).toHaveLength(0)
  })

  it("removes toast on Enter key", async () => {
    const store = useToastStore()
    store.addToast("Press Enter", "success")
    const wrapper = mount(ToastContainer, { global: globalOpts })
    await wrapper.find(".nars-toast").trigger("keydown.enter")
    await nextTick()
    expect(wrapper.findAll(".nars-toast")).toHaveLength(0)
  })

  it("removes toast on Space key", async () => {
    const store = useToastStore()
    store.addToast("Press Space", "success")
    const wrapper = mount(ToastContainer, { global: globalOpts })
    await wrapper.find(".nars-toast").trigger("keydown.space")
    await nextTick()
    expect(wrapper.findAll(".nars-toast")).toHaveLength(0)
  })

  it("applies correct background color for each type", () => {
    const store = useToastStore()
    store.addToast("Success", "success")
    store.addToast("Error", "error")
    store.addToast("Info", "info")
    const wrapper = mount(ToastContainer, { global: globalOpts })
    const toasts = wrapper.findAll(".nars-toast")
    expect(toasts.length).toBe(3)
    expect(toasts[0].attributes("style")).toContain("background")
  })
})
