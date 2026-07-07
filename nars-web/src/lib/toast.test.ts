import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { showToast, showConfirm } from "./toast"
import { useToastStore } from "../stores/toastStore"
import { useConfirmStore } from "../stores/confirmStore"

beforeEach(() => {
  setActivePinia(createPinia())
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

describe("showToast", () => {
  it("adds a toast to the store", () => {
    showToast("Hello, world!", "info")
    const store = useToastStore()
    expect(store.toasts).toHaveLength(1)
    expect(store.toasts[0].message).toBe("Hello, world!")
    expect(store.toasts[0].type).toBe("info")
  })

  it("applies the correct type", () => {
    showToast("Success!", "success")
    showToast("Error!", "error")
    showToast("Info!", "info")
    const store = useToastStore()
    expect(store.toasts).toHaveLength(3)
    expect(store.toasts[0].type).toBe("success")
    expect(store.toasts[1].type).toBe("error")
    expect(store.toasts[2].type).toBe("info")
  })

  it("auto-dismisses after duration", () => {
    showToast("Test", "info")
    const store = useToastStore()
    expect(store.toasts).toHaveLength(1)
    vi.advanceTimersByTime(3500)
    expect(store.toasts).toHaveLength(0)
  })

  it("dismisses on removeToast", () => {
    const id = showToast("Click me", "info")
    const store = useToastStore()
    expect(store.toasts).toHaveLength(1)
    store.removeToast(id)
    expect(store.toasts).toHaveLength(0)
  })

  it("queues multiple toasts", () => {
    showToast("First", "info")
    showToast("Second", "success")
    const store = useToastStore()
    expect(store.toasts).toHaveLength(2)
  })
})

describe("showConfirm", () => {
  it("opens the confirm store with message and okText", () => {
    showConfirm("Are you sure?")
    const store = useConfirmStore()
    expect(store.visible).toBe(true)
    expect(store.message).toBe("Are you sure?")
    expect(store.okText).toBe("Confirm")
  })

  it("uses custom ok button text", () => {
    showConfirm("Proceed?", "Yes, delete")
    const store = useConfirmStore()
    expect(store.okText).toBe("Yes, delete")
  })

  it("resolves true on confirm", async () => {
    const promise = showConfirm("Go?")
    const store = useConfirmStore()
    store.confirm()
    const result = await promise
    expect(result).toBe(true)
    expect(store.visible).toBe(false)
  })

  it("resolves false on cancel", async () => {
    const promise = showConfirm("Go?")
    const store = useConfirmStore()
    store.cancel()
    const result = await promise
    expect(result).toBe(false)
    expect(store.visible).toBe(false)
  })

  it("handles multiple confirm calls sequentially", async () => {
    const p1 = showConfirm("First?")
    const store = useConfirmStore()
    store.confirm()
    expect(await p1).toBe(true)

    const p2 = showConfirm("Second?")
    expect(store.visible).toBe(true)
    expect(store.message).toBe("Second?")
    store.cancel()
    expect(await p2).toBe(false)
    expect(store.visible).toBe(false)
  })
})
