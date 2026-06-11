import { describe, it, expect, vi, beforeEach } from "vitest"
import { addBoundaryClickEvents, resetBoundaryEvents } from "./map-boundary"
import type maplibregl from "maplibre-gl"

const mockMapOn = vi.fn()
const mockGetCanvas = vi.fn(() => ({
  style: {
    setProperty: vi.fn(),
    removeProperty: vi.fn(),
  },
}))

vi.mock("../lib/toast", () => ({
  showToast: vi.fn(),
}))

vi.mock("./core/state", () => ({ ctx: {} }))

beforeEach(() => {
  vi.clearAllMocks()
  resetBoundaryEvents()

  mockMapOn.mockImplementation((_event: string, _layer: unknown, _handler: unknown) => ({
    remove: vi.fn(),
  }))
})

function makeMap() {
  return {
    on: mockMapOn,
    off: vi.fn(),
    getCanvas: mockGetCanvas,
  } as unknown as maplibregl.Map
}

describe("map-boundary", () => {
  it("registers click, mouseenter, mouseleave, and contextmenu handlers", () => {
    addBoundaryClickEvents(makeMap())
    expect(mockMapOn).toHaveBeenCalledWith("click", "nars-boundaries", expect.any(Function))
    expect(mockMapOn).toHaveBeenCalledWith("mouseenter", "nars-boundaries", expect.any(Function))
    expect(mockMapOn).toHaveBeenCalledWith("mouseleave", "nars-boundaries", expect.any(Function))
    expect(mockMapOn).toHaveBeenCalledWith("contextmenu", "nars-boundaries", expect.any(Function))
  })

  it("does not register events twice", () => {
    addBoundaryClickEvents(makeMap())
    addBoundaryClickEvents(makeMap())
    expect(mockMapOn).toHaveBeenCalledTimes(4)
  })

  it("re-registers after resetBoundaryEvents", () => {
    addBoundaryClickEvents(makeMap())
    resetBoundaryEvents()
    addBoundaryClickEvents(makeMap())
    expect(mockMapOn).toHaveBeenCalledTimes(8)
  })
})
