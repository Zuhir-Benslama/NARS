import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { showToast, showConfirm } from "./toast"

beforeEach(() => {
  vi.useFakeTimers()
  document.querySelectorAll(".nars-confirm-backdrop").forEach((el) => el.remove())
  const tc = document.getElementById("nars-toast-container")
  if (tc) tc.innerHTML = ""
})

afterEach(() => {
  vi.useRealTimers()
})

function triggerTransitionEnd(el: HTMLElement): void {
  el.dispatchEvent(new Event("transitionend"))
}

describe("showToast", () => {
  it("creates a toast element in the container", () => {
    showToast("Hello, world!", "info")
    vi.advanceTimersByTime(100)
    const container = document.getElementById("nars-toast-container")
    expect(container).not.toBeNull()
    expect(container!.childElementCount).toBe(1)
    expect(container!.textContent).toContain("Hello, world!")
  })

  it("applies the correct background color per type", () => {
    showToast("Success!", "success")
    vi.advanceTimersByTime(100)
    const container = document.getElementById("nars-toast-container")!
    const toast = container.firstElementChild as HTMLElement
    expect(toast.style.background).toBe("rgb(34, 197, 94)")
  })

  it("applies error background", () => {
    showToast("Error!", "error")
    vi.advanceTimersByTime(100)
    const container = document.getElementById("nars-toast-container")!
    const toast = container.firstElementChild as HTMLElement
    expect(toast.style.background).toBe("rgb(239, 68, 68)")
  })

  it("applies info background", () => {
    showToast("Info!", "info")
    vi.advanceTimersByTime(100)
    const container = document.getElementById("nars-toast-container")!
    const toast = container.firstElementChild as HTMLElement
    expect(toast.style.background).toBe("rgb(59, 130, 246)")
  })

  it("auto-dismisses after duration", () => {
    showToast("Test", "info")
    vi.advanceTimersByTime(100)
    const container = document.getElementById("nars-toast-container")!
    expect(container.childElementCount).toBe(1)
    vi.advanceTimersByTime(3500)
    const toast = container.firstElementChild as HTMLElement
    triggerTransitionEnd(toast)
    expect(container.childElementCount).toBe(0)
  })

  it("dismisses on click", () => {
    showToast("Click me", "info")
    vi.advanceTimersByTime(100)
    const container = document.getElementById("nars-toast-container")!
    const toast = container.firstElementChild as HTMLElement
    toast.click()
    triggerTransitionEnd(toast)
    expect(container.childElementCount).toBe(0)
  })

  it("queue multiple toasts", () => {
    showToast("First", "info")
    showToast("Second", "success")
    vi.advanceTimersByTime(100)
    const container = document.getElementById("nars-toast-container")!
    expect(container.childElementCount).toBe(2)
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
