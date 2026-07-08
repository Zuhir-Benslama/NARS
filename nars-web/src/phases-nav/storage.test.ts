import { describe, it, expect, beforeEach, vi } from "vitest"

function freshStorage() {
  return import("./storage")
}

describe("getPhaseStorageKey", () => {
  it("returns base key when communeId is null", async () => {
    const { getPhaseStorageKey } = await freshStorage()
    expect(getPhaseStorageKey(null)).toBe("nars_current_phase")
  })

  it("returns base key when communeId is undefined", async () => {
    const { getPhaseStorageKey } = await freshStorage()
    expect(getPhaseStorageKey(undefined)).toBe("nars_current_phase")
  })

  it("appends communeId when provided", async () => {
    const { getPhaseStorageKey } = await freshStorage()
    expect(getPhaseStorageKey(42)).toBe("nars_current_phase_42")
  })

  it("handles string communeId", async () => {
    const { getPhaseStorageKey } = await freshStorage()
    expect(getPhaseStorageKey("abc")).toBe("nars_current_phase_abc")
  })
})

describe("savePhase", () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it("saves phase index to localStorage", async () => {
    const { savePhase, loadPhase } = await freshStorage()
    savePhase(3)
    expect(loadPhase()).toBe(3)
  })

  it("saves phase with commune-specific key", async () => {
    const { savePhase, loadPhase } = await freshStorage()
    savePhase(5, 10)
    expect(loadPhase(10)).toBe(5)
    expect(loadPhase()).toBeNull()
  })

  it("handles localStorage errors gracefully", async () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("Quota exceeded")
    })
    const { savePhase } = await freshStorage()
    expect(() => savePhase(1)).not.toThrow()
    vi.restoreAllMocks()
  })
})

describe("loadPhase", () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it("returns null when no phase is saved", async () => {
    const { loadPhase } = await freshStorage()
    expect(loadPhase()).toBeNull()
  })

  it("returns saved phase index", async () => {
    const { savePhase, loadPhase } = await freshStorage()
    savePhase(7)
    expect(loadPhase()).toBe(7)
  })

  it("returns null for invalid stored value", async () => {
    const { getPhaseStorageKey, loadPhase } = await freshStorage()
    localStorage.setItem(getPhaseStorageKey(), "not-a-number")
    expect(loadPhase()).toBeNull()
  })

  it("handles localStorage errors gracefully", async () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("Storage error")
    })
    const { loadPhase } = await freshStorage()
    expect(loadPhase()).toBeNull()
    vi.restoreAllMocks()
  })
})
