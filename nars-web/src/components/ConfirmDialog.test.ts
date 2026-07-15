import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"
import { setActivePinia, createPinia } from "pinia"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import ConfirmDialog from "./ConfirmDialog.vue"
import { useConfirmStore } from "../stores/confirmStore"

const globalOpts = {
  stubs: {
    Teleport: { template: "<div><slot /></div>" },
  },
}

describe("ConfirmDialog", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("is hidden when store.visible is false", () => {
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    expect(wrapper.find(".confirm-backdrop").exists()).toBe(false)
  })

  it("shows when store.visible is true", async () => {
    const store = useConfirmStore()
    store.visible = true
    store.message = "Are you sure?"
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    expect(wrapper.find(".confirm-backdrop").exists()).toBe(true)
  })

  it("displays the message", async () => {
    const store = useConfirmStore()
    store.visible = true
    store.message = "Delete this item?"
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    expect(wrapper.find(".confirm-message").text()).toBe("Delete this item?")
  })

  it("displays default okText", async () => {
    const store = useConfirmStore()
    store.visible = true
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    expect(wrapper.find(".confirm-btn.ok").text()).toBe("Confirm")
  })

  it("displays custom okText", async () => {
    const store = useConfirmStore()
    store.visible = true
    store.okText = "Delete"
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    expect(wrapper.find(".confirm-btn.ok").text()).toBe("Delete")
  })

  it("calls store.confirm on OK click", async () => {
    const store = useConfirmStore()
    store.visible = true
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    await wrapper.find(".confirm-btn.ok").trigger("click")
    expect(store.visible).toBe(false)
  })

  it("calls store.cancel on Cancel click", async () => {
    const store = useConfirmStore()
    store.visible = true
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    await wrapper.find(".confirm-btn.cancel").trigger("click")
    expect(store.visible).toBe(false)
  })

  it("calls store.cancel on backdrop click", async () => {
    const store = useConfirmStore()
    store.visible = true
    const wrapper = mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    await wrapper.find(".confirm-backdrop").trigger("click")
    expect(store.visible).toBe(false)
  })

  it("calls store.cancel on Escape key", async () => {
    const store = useConfirmStore()
    store.visible = true
    mount(ConfirmDialog, { global: globalOpts })
    await nextTick()
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }))
    expect(store.visible).toBe(false)
  })

  it("does not react to Escape when hidden", () => {
    const store = useConfirmStore()
    store.visible = false
    mount(ConfirmDialog, { global: globalOpts })
    store.visible = true
    store.visible = false
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }))
    expect(store.visible).toBe(false)
  })
})
