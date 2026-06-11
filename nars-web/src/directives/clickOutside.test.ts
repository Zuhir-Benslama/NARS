import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { vClickOutside } from "./clickOutside"

describe("vClickOutside", () => {
  let el: HTMLElement
  let handler: ReturnType<typeof vi.fn>

  const dir = vClickOutside as Record<string, Function>

  beforeEach(() => {
    el = document.createElement("div")
    el.dataset.testid = "target"
    handler = vi.fn()
    document.body.innerHTML = ""
  })

  afterEach(() => {
    dir.unmounted(el)
    document.body.innerHTML = ""
  })

  it("calls handler when clicking outside the element", () => {
    dir.mounted(el, {
      value: handler,
      modifiers: {},
      instance: null,
      dir: {} as any,
      oldValue: undefined,
    })

    document.body.appendChild(el)
    document.body.click()
    expect(handler).toHaveBeenCalledTimes(1)
  })

  it("does not call handler when clicking inside the element", () => {
    dir.mounted(el, {
      value: handler,
      modifiers: {},
      instance: null,
      dir: {} as any,
      oldValue: undefined,
    })

    document.body.appendChild(el)
    el.click()
    expect(handler).not.toHaveBeenCalled()
  })

  it("cleans up event listener on unmount", () => {
    dir.mounted(el, {
      value: handler,
      modifiers: {},
      instance: null,
      dir: {} as any,
      oldValue: undefined,
    })

    document.body.appendChild(el)
    dir.unmounted(el)

    document.body.click()
    expect(handler).not.toHaveBeenCalled()
  })
})
