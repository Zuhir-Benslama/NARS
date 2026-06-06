import { describe, it, expect, vi } from "vitest"
import { debugLog, debugError, debugWarn, debugInfo, isDebugEnabled } from "./debug"

describe("debug", () => {
  it("isDebugEnabled returns true in vitest mode", () => {
    expect(isDebugEnabled()).toBe(true)
  })

  it("debugLog calls console.log", () => {
    const spy = vi.spyOn(console, "log")
    debugLog("test message")
    expect(spy).toHaveBeenCalledWith("test message")
    spy.mockRestore()
  })

  it("debugLog with multiple args", () => {
    const spy = vi.spyOn(console, "log")
    debugLog("a", "b", "c")
    expect(spy).toHaveBeenCalledWith("a", "b", "c")
    spy.mockRestore()
  })

  it("debugError calls console.error", () => {
    const spy = vi.spyOn(console, "error")
    debugError("error message")
    expect(spy).toHaveBeenCalledWith("error message")
    spy.mockRestore()
  })

  it("debugWarn calls console.warn", () => {
    const spy = vi.spyOn(console, "warn")
    debugWarn("warning message")
    expect(spy).toHaveBeenCalledWith("warning message")
    spy.mockRestore()
  })

  it("debugInfo calls console.info", () => {
    const spy = vi.spyOn(console, "info")
    debugInfo("info message")
    expect(spy).toHaveBeenCalledWith("info message")
    spy.mockRestore()
  })
})
