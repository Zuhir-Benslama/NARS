import { describe, it, expect, vi, afterEach } from "vitest"
import { defineComponent, h, nextTick, ref, type ComponentPublicInstance } from "vue"
import { mount } from "@vue/test-utils"
import { useFocusTrap } from "./useFocusTrap"

function TrapHarness() {
  return defineComponent({
    setup() {
      const visible = ref(false)
      const container = ref<HTMLElement | null>(null)
      const outside = ref<HTMLElement | null>(null)
      useFocusTrap(container, () => visible.value)
      return { visible, container, outside }
    },
    render() {
      const toEl = (el: Element | ComponentPublicInstance | null) =>
        el instanceof HTMLElement ? el : null
      return h("div", [
        h(
          "button",
          {
            ref: (el: Element | ComponentPublicInstance | null) => {
              this.outside = toEl(el)
            },
          },
          "outside",
        ),
        this.visible
          ? h(
              "div",
              {
                ref: (el: Element | ComponentPublicInstance | null) => {
                  this.container = toEl(el)
                },
              },
              [h("button", { class: "first" }, "first"), h("input", { class: "second" })],
            )
          : null,
      ])
    },
  })
}

describe("useFocusTrap", () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("does not install the trap while the container is hidden", () => {
    const addListener = vi.spyOn(window, "addEventListener")
    const wrapper = mount(TrapHarness(), { attachTo: document.body })
    expect(wrapper.find(".first").exists()).toBe(false)
    expect(addListener).not.toHaveBeenCalled()
  })

  it("installs the trap and moves focus in when it becomes active", async () => {
    const wrapper = mount(TrapHarness(), { attachTo: document.body })
    const addListener = vi.spyOn(window, "addEventListener")
    const wrapperVM = wrapper.vm as unknown as { visible: boolean }
    wrapperVM.visible = true
    await nextTick()
    await nextTick()
    expect(wrapper.find(".first").exists()).toBe(true)
    expect(addListener).toHaveBeenCalledWith("keydown", expect.any(Function))
    expect(document.activeElement?.className).toBe("first")
  })

  it("restores focus to the previously active element on teardown", async () => {
    const wrapper = mount(TrapHarness(), { attachTo: document.body })
    const outsideBtn = wrapper.find("button")
    outsideBtn.element.focus()
    const wrapperVM = wrapper.vm as unknown as { visible: boolean }
    wrapperVM.visible = true
    await nextTick()
    await nextTick()
    wrapperVM.visible = false
    await nextTick()
    await nextTick()
    expect(document.activeElement).toBe(outsideBtn.element)
  })

  it("re-installs the trap on a subsequent open", async () => {
    const wrapper = mount(TrapHarness(), { attachTo: document.body })
    const wrapperVM = wrapper.vm as unknown as { visible: boolean }
    wrapperVM.visible = true
    await nextTick()
    await nextTick()
    wrapperVM.visible = false
    await nextTick()
    await nextTick()
    wrapperVM.visible = true
    await nextTick()
    await nextTick()
    expect(document.activeElement?.className).toBe("first")
  })
})
