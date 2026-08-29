import { describe, it, expect, vi, beforeEach } from "vitest"

let mod: typeof import("./geoman-types")

beforeEach(async () => {
  vi.resetModules()
  mod = await import("./geoman-types")
})

describe("asGeomanInternal", () => {
  it("passes through a geoman instance", () => {
    const gm = { someMethod: vi.fn() }
    expect(mod.asGeomanInternal(gm)).toBe(gm)
  })

  it("returns null for null", () => {
    expect(mod.asGeomanInternal(null)).toBeNull()
  })
})

describe("isGeomanCreateEvent", () => {
  it("narrows an object with a shape property", () => {
    expect(mod.isGeomanCreateEvent({ shape: "polygon" })).toBe(true)
  })

  it("rejects objects without a shape property", () => {
    expect(mod.isGeomanCreateEvent({ feature: {} })).toBe(false)
    expect(mod.isGeomanCreateEvent(null)).toBe(false)
    expect(mod.isGeomanCreateEvent("string")).toBe(false)
    expect(mod.isGeomanCreateEvent(undefined)).toBe(false)
  })
})

describe("isGeomanEditEvent", () => {
  it("narrows an object with a feature property", () => {
    expect(mod.isGeomanEditEvent({ feature: {} })).toBe(true)
  })

  it("rejects objects without a feature property", () => {
    expect(mod.isGeomanEditEvent({ shape: "polygon" })).toBe(false)
    expect(mod.isGeomanEditEvent(null)).toBe(false)
  })
})

describe("isGeomanRemoveEvent", () => {
  it("narrows an object with a feature property", () => {
    expect(mod.isGeomanRemoveEvent({ feature: { id: 1 } })).toBe(true)
  })

  it("rejects objects without a feature property", () => {
    expect(mod.isGeomanRemoveEvent({ markerIndex: 0 })).toBe(false)
    expect(mod.isGeomanRemoveEvent(null)).toBe(false)
  })
})

describe("isGeomanMarkerDragEvent", () => {
  it("narrows an object with markerIndex or vertexIndex", () => {
    expect(mod.isGeomanMarkerDragEvent({ markerIndex: 0 })).toBe(true)
    expect(mod.isGeomanMarkerDragEvent({ vertexIndex: 2 })).toBe(true)
  })

  it("rejects objects without markerIndex or vertexIndex", () => {
    expect(mod.isGeomanMarkerDragEvent({ feature: {} })).toBe(false)
    expect(mod.isGeomanMarkerDragEvent(null)).toBe(false)
  })
})
