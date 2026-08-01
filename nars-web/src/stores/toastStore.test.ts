import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useToastStore } from "./toastStore"
import { UI_CONFIG } from "../config"

describe("toastStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it("addToast pushes a toast and increments ids", () => {
    const store = useToastStore()
    const first = store.addToast("Hello", "info")
    const second = store.addToast("World", "success")
    expect(first).toBe(1)
    expect(second).toBe(2)
    expect(store.toasts).toEqual([
      { id: 1, message: "Hello", type: "info" },
      { id: 2, message: "World", type: "success" },
    ])
  })

  it("addToast registers a timer and auto-removes after the configured duration", () => {
    const store = useToastStore()
    const id = store.addToast("Transient", "error")
    expect(store.timers[id]).toBeTypeOf("object")
    expect(store.toasts).toHaveLength(1)
    vi.advanceTimersByTime(UI_CONFIG.toastDuration)
    expect(store.toasts).toHaveLength(0)
    expect(store.timers[id]).toBeUndefined()
  })

  it("removeToast clears the timer and removes the toast", () => {
    const store = useToastStore()
    const id = store.addToast("Gone", "info")
    store.removeToast(id)
    expect(store.toasts).toHaveLength(0)
    expect(store.timers[id]).toBeUndefined()
  })

  it("removeToast with an unknown id is a no-op", () => {
    const store = useToastStore()
    store.addToast("Keep", "info")
    store.removeToast(999)
    expect(store.toasts).toHaveLength(1)
  })

  it("removeToast still removes the toast when no timer is registered", () => {
    const store = useToastStore()
    const id = store.addToast("No timer", "info")
    store.timers[id] = undefined as never
    store.removeToast(id)
    expect(store.toasts).toHaveLength(0)
  })

  it("clearAll cancels timers and empties toasts", () => {
    const store = useToastStore()
    store.addToast("A", "info")
    store.addToast("B", "error")
    store.clearAll()
    expect(store.toasts).toHaveLength(0)
    expect(store.timers).toEqual({})
  })

  it("reset delegates to clearAll", () => {
    const store = useToastStore()
    store.addToast("A", "success")
    store.reset()
    expect(store.toasts).toHaveLength(0)
    expect(store.timers).toEqual({})
  })
})
