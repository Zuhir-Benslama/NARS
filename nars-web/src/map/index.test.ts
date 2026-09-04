import { describe, it, expect, vi, beforeEach } from "vitest"

const {
  mockMapRemove,
  mockDestroyDrawEvents,
  mockUnregisterGeomanEvents,
  mockUnregisterFieldWorkerClick,
  mockRemoveBoundaryClickEvents,
  mockDisposeGeoman,
} = vi.hoisted(() => ({
  mockMapRemove: vi.fn(),
  mockDestroyDrawEvents: vi.fn(),
  mockUnregisterGeomanEvents: vi.fn(),
  mockUnregisterFieldWorkerClick: vi.fn(),
  mockRemoveBoundaryClickEvents: vi.fn(),
  mockDisposeGeoman: vi.fn(),
}))

vi.mock("./core/state", () => ({
  getCtx: () => ({
    map: { remove: mockMapRemove },
    geoman: undefined,
  }),
  tryGetCtx: () => ({
    map: { remove: mockMapRemove },
    geoman: undefined,
  }),
}))

vi.mock("../i18n", () => ({ applyInitialLang: vi.fn() }))
vi.mock("./rotation", () => ({
  initRotationControls: vi.fn(),
  destroyRotationControls: vi.fn(),
}))
vi.mock("./draw/draw-events", () => ({
  registerDrawEvents: vi.fn(),
  destroyDrawEvents: mockDestroyDrawEvents,
}))
vi.mock("./core/geoman-events", () => ({
  registerGeomanEvents: vi.fn(),
  unregisterGeomanEvents: mockUnregisterGeomanEvents,
}))
vi.mock("./map-init", () => ({
  initMap: vi.fn(),
  setBaseLayer: vi.fn(),
  disposeGeoman: mockDisposeGeoman,
}))
vi.mock("./field-click", () => ({
  registerFieldWorkerClick: vi.fn(),
  unregisterFieldWorkerClick: mockUnregisterFieldWorkerClick,
}))
vi.mock("./map-boundary", () => ({
  removeBoundaryClickEvents: mockRemoveBoundaryClickEvents,
}))

import { destroyMap } from "./index"

describe("destroyMap", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("disposes Geoman and removes the map instance", async () => {
    await destroyMap()

    expect(mockDisposeGeoman).toHaveBeenCalledTimes(1)
    expect(mockMapRemove).toHaveBeenCalledTimes(1)
  })

  it("unregisters all map event handlers", async () => {
    await destroyMap()

    expect(mockDestroyDrawEvents).toHaveBeenCalledTimes(1)
    expect(mockUnregisterGeomanEvents).toHaveBeenCalledTimes(1)
    expect(mockUnregisterFieldWorkerClick).toHaveBeenCalledTimes(1)
    expect(mockRemoveBoundaryClickEvents).toHaveBeenCalledTimes(1)
  })

  it("removes the map before disposing Geoman (sync beforeunload safety)", async () => {
    const order: string[] = []
    mockDisposeGeoman.mockImplementation(() => {
      order.push("dispose")
      return Promise.resolve()
    })
    mockMapRemove.mockImplementation(() => {
      order.push("remove")
    })

    await destroyMap()

    expect(order).toEqual(["remove", "dispose"])
  })

  it("is a no-op when the map was never initialized", async () => {
    const state = await import("./core/state")
    vi.spyOn(state, "tryGetCtx").mockReturnValue(null)

    await destroyMap()

    expect(mockDestroyDrawEvents).not.toHaveBeenCalled()
    expect(mockUnregisterGeomanEvents).not.toHaveBeenCalled()
    expect(mockUnregisterFieldWorkerClick).not.toHaveBeenCalled()
    expect(mockRemoveBoundaryClickEvents).not.toHaveBeenCalled()
    expect(mockDisposeGeoman).not.toHaveBeenCalled()
    expect(mockMapRemove).not.toHaveBeenCalled()
  })
})
