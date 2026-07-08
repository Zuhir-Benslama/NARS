import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"
import { setActivePinia, createPinia } from "pinia"

import ContextMenu from "./ContextMenu.vue"
import { useContextMenuStore } from "../stores/contextMenuStore"

const globalOpts = {
  stubs: { Teleport: { template: '<div><slot /></div>' } },
}

describe("ContextMenu", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("is hidden by default", () => {
    const wrapper = mount(ContextMenu, { global: globalOpts })
    expect(wrapper.find(".nars-ctx-menu").exists()).toBe(false)
  })

  it("shows when store.visible is true", async () => {
    const store = useContextMenuStore()
    store.show(100, 200, [{ label: "Edit" }])
    const wrapper = mount(ContextMenu, { global: globalOpts })
    await nextTick()
    expect(wrapper.find(".nars-ctx-menu").exists()).toBe(true)
  })

  it("renders menu items", async () => {
    const store = useContextMenuStore()
    store.show(100, 200, [{ label: "Edit" }, { label: "Delete", danger: true }])
    const wrapper = mount(ContextMenu, { global: globalOpts })
    await nextTick()
    const items = wrapper.findAll(".ctx-item")
    expect(items).toHaveLength(2)
    expect(items[0].text()).toBe("Edit")
    expect(items[1].text()).toBe("Delete")
  })

  it("applies ctx-danger class for dangerous items", async () => {
    const store = useContextMenuStore()
    store.show(100, 200, [{ label: "Delete", danger: true }])
    const wrapper = mount(ContextMenu, { global: globalOpts })
    await nextTick()
    expect(wrapper.find(".ctx-item").classes()).toContain("ctx-danger")
  })

  it("renders separators", async () => {
    const store = useContextMenuStore()
    store.show(100, 200, [{ label: "Edit" }, { separator: true }, { label: "Delete" }])
    const wrapper = mount(ContextMenu, { global: globalOpts })
    await nextTick()
    expect(wrapper.findAll(".ctx-separator")).toHaveLength(1)
  })

  it("calls item.onClick and hides on click", async () => {
    const onClick = vi.fn()
    const store = useContextMenuStore()
    store.show(100, 200, [{ label: "Edit", onClick }])
    const wrapper = mount(ContextMenu, { global: globalOpts })
    await nextTick()
    await wrapper.find(".ctx-item").trigger("click")
    expect(onClick).toHaveBeenCalledOnce()
    expect(store.visible).toBe(false)
  })

  it("positions menu at store coordinates", async () => {
    const store = useContextMenuStore()
    store.show(50, 75, [{ label: "Test" }])
    const wrapper = mount(ContextMenu, { global: globalOpts })
    await nextTick()
    const menu = wrapper.find(".nars-ctx-menu")
    expect(menu.attributes("style")).toContain("left: 50px")
    expect(menu.attributes("style")).toContain("top: 75px")
  })

  it("hides on document click", async () => {
    const store = useContextMenuStore()
    const wrapper = mount(ContextMenu, { global: globalOpts })
    store.show(100, 200, [{ label: "Test" }])
    await nextTick()
    document.dispatchEvent(new MouseEvent("click", { bubbles: true }))
    expect(store.visible).toBe(false)
    wrapper.unmount()
  })

  it("hides on Escape key", async () => {
    const store = useContextMenuStore()
    const wrapper = mount(ContextMenu, { global: globalOpts })
    store.show(100, 200, [{ label: "Test" }])
    await nextTick()
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }))
    expect(store.visible).toBe(false)
    wrapper.unmount()
  })
})
