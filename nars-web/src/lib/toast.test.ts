import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { showToast, showConfirm } from "./toast"
import { useToastStore } from "../stores/toastStore"

beforeEach(() => {
  setActivePinia(createPinia())
  vi.useFakeTimers()
  document.querySelectorAll(".nars-confirm-backdrop").forEach((el) => el.remove())
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
  it("creates a dialog with message and buttons", () => {
    showConfirm("Are you sure?")
    vi.advanceTimersByTime(100)
    expect(document.querySelector(".nars-confirm-backdrop")).not.toBeNull()
    expect(document.querySelector(".nars-confirm-dialog")).not.toBeNull()
    expect(document.body.textContent).toContain("Are you sure?")
    expect(document.body.textContent).toContain("Cancel")
    expect(document.body.textContent).toContain("Confirm")
  })

  it("uses custom ok button text", () => {
    showConfirm("Proceed?", "Yes, delete")
    vi.advanceTimersByTime(100)
    expect(document.body.textContent).toContain("Yes, delete")
  })

  it("resolves true on OK button click", async () => {
    const promise = showConfirm("Go?")
    vi.advanceTimersByTime(100)
    const okBtn = document.querySelector(".nars-confirm-dialog button:last-child") as HTMLElement
    okBtn.click()
    const result = await promise
    expect(result).toBe(true)
  })

  it("resolves false on Cancel button click", async () => {
    const promise = showConfirm("Go?")
    vi.advanceTimersByTime(100)
    const cancelBtn = document.querySelector(
      ".nars-confirm-dialog button:first-child",
    ) as HTMLElement
    cancelBtn.click()
    const result = await promise
    expect(result).toBe(false)
  })

  it("resolves false on Escape key", async () => {
    const promise = showConfirm("Go?")
    vi.advanceTimersByTime(100)
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }))
    const result = await promise
    expect(result).toBe(false)
  })

  it("resolves false on backdrop click", async () => {
    const promise = showConfirm("Go?")
    vi.advanceTimersByTime(100)
    const backdrop = document.querySelector(".nars-confirm-backdrop") as HTMLElement
    backdrop.click()
    const result = await promise
    expect(result).toBe(false)
  })
})
