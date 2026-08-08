import { describe, it, expect, beforeEach, vi } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useContextMenuStore, resetContextMenuStore } from "./contextMenuStore"
import type { CtxMenuItem } from "./contextMenuStore"

describe("contextMenuStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("starts hidden with no items", () => {
    const store = useContextMenuStore()
    expect(store.visible).toBe(false)
    expect(store.x).toBe(0)
    expect(store.y).toBe(0)
    expect(store.items).toEqual([])
  })

  it("show sets position, items and visibility", () => {
    const store = useContextMenuStore()
    const items: CtxMenuItem[] = [
      { label: "Edit", onClick: vi.fn() },
      { separator: true },
      { label: "Delete", danger: true },
    ]
    store.show(120, 340, items)
    expect(store.visible).toBe(true)
    expect(store.x).toBe(120)
    expect(store.y).toBe(340)
    expect(store.items).toEqual(items)
  })

  it("hide clears items and hides the menu", () => {
    const store = useContextMenuStore()
    store.show(10, 20, [{ label: "Edit" }])
    store.hide()
    expect(store.visible).toBe(false)
    expect(store.items).toEqual([])
  })

  it("hide preserves the last position", () => {
    const store = useContextMenuStore()
    store.show(10, 20, [{ label: "Edit" }])
    store.hide()
    expect(store.x).toBe(10)
    expect(store.y).toBe(20)
  })

  it("reset clears position, items and visibility", () => {
    const store = useContextMenuStore()
    store.show(50, 60, [{ label: "Edit" }])
    store.reset()
    expect(store.visible).toBe(false)
    expect(store.x).toBe(0)
    expect(store.y).toBe(0)
    expect(store.items).toEqual([])
  })

  it("reset is idempotent on a fresh store", () => {
    const store = useContextMenuStore()
    store.reset()
    expect(store.visible).toBe(false)
    expect(store.x).toBe(0)
    expect(store.y).toBe(0)
  })

  it("resetContextMenuStore resets the active store", () => {
    const store = useContextMenuStore()
    store.show(7, 8, [{ label: "Edit" }])
    resetContextMenuStore()
    expect(useContextMenuStore().visible).toBe(false)
    expect(useContextMenuStore().x).toBe(0)
    expect(useContextMenuStore().y).toBe(0)
    expect(useContextMenuStore().items).toEqual([])
  })
})
