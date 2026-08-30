import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"

const {
  mockDebugLog,
  mockDebugError,
  mockEnsureGeomanDrawEdgesVisible,
  mockPatchGeomanMarkerPointerSnap,
  mockDelay,
} = vi.hoisted(() => ({
  mockDebugLog: vi.fn(),
  mockDebugError: vi.fn(),
  mockEnsureGeomanDrawEdgesVisible: vi.fn(),
  mockPatchGeomanMarkerPointerSnap: vi.fn(),
  mockDelay: vi.fn(async () => {}),
}))

const { mockGetCtx, getSetCtx } = vi.hoisted(() => {
  let current: any = null
  return {
    mockGetCtx: vi.fn(() => current),
    getSetCtx: (c: any) => {
      current = c
    },
  }
})

vi.mock("../../utils/debug", () => ({
  debugLog: mockDebugLog,
  debugError: mockDebugError,
}))

vi.mock("../edit/edit-state", () => ({
  ensureGeomanDrawEdgesVisible: mockEnsureGeomanDrawEdgesVisible,
}))

vi.mock("./draw-marker-patch", () => ({
  patchGeomanMarkerPointerSnap: mockPatchGeomanMarkerPointerSnap,
}))

vi.mock("../../utils/time", () => ({
  delay: mockDelay,
}))

vi.mock("../core/state", () => ({
  getCtx: mockGetCtx,
}))

import { resetDrawControl, clearEdgeVisibilityPoll, buildDrawControl } from "./draw-control"

let useDrawStore: any

beforeEach(async () => {
  vi.useFakeTimers()
  vi.clearAllMocks()
  setActivePinia(createPinia())
  const ds = await import("../../stores/drawStore")
  useDrawStore = ds.useDrawStore
  getSetCtx({ geoman: null })
})

afterEach(() => {
  getSetCtx(null)
  vi.useRealTimers()
})

function setGeoman(geoman: any) {
  getSetCtx({ geoman })
}

function polygonPhase() {
  return { key: "areas", drawType: "polygon", color: "#f00" }
}

describe("resetDrawControl", () => {
  it("clears the last phase key and resets the mode-switch token", () => {
    const store = useDrawStore()
    store.setLastPhaseKey("roads")
    store.setModeSwitchToken(3)
    resetDrawControl()
    expect(store.lastPhaseKey).toBeNull()
    expect(store.modeSwitchToken).toBe(0)
  })
})

describe("clearEdgeVisibilityPoll", () => {
  it("clears the edge poll interval and timeout ids", () => {
    const store = useDrawStore()
    const pollId = setInterval(() => {}, 100)
    const timeoutId = setTimeout(() => {}, 100)
    store.setEdgePollId(pollId)
    store.setEdgeTimeoutId(timeoutId as any)
    clearEdgeVisibilityPoll()
    expect(store.edgePollId).toBeNull()
    expect(store.edgeTimeoutId).toBeNull()
  })

  it("is a no-op when nothing is pending", () => {
    const store = useDrawStore()
    clearEdgeVisibilityPoll()
    expect(store.edgePollId).toBeNull()
    expect(store.edgeTimeoutId).toBeNull()
  })
})

describe("buildDrawControl", () => {
  it("patches snapping but no-ops when geoman is not present", async () => {
    await buildDrawControl(polygonPhase())
    expect(mockPatchGeomanMarkerPointerSnap).toHaveBeenCalled()
  })

  it("no-ops for an unmapped drawType", async () => {
    const gm = {
      getActiveDrawModes: vi.fn(() => []),
      disableDraw: vi.fn(async () => {}),
      enableDraw: vi.fn(async () => {}),
    }
    setGeoman(gm)
    await buildDrawControl({ key: "namingPanels", drawType: "bogus", color: "#fff" })
    expect(mockDelay).not.toHaveBeenCalled()
  })

  it("returns early when already in the correct draw mode", async () => {
    const gm = {
      getActiveDrawModes: vi.fn(() => ["polygon"]),
      disableDraw: vi.fn(async () => {}),
      enableDraw: vi.fn(async () => {}),
    }
    setGeoman(gm)
    const store = useDrawStore()
    store.setLastPhaseKey("areas")
    await buildDrawControl(polygonPhase())
    expect(mockDelay).not.toHaveBeenCalled()
    expect(gm.disableDraw).not.toHaveBeenCalled()
  })

  it("enables the shape after disabling active modes and the settle delay", async () => {
    const gm = {
      getActiveDrawModes: vi.fn(() => ["line"]),
      disableDraw: vi.fn(async () => {}),
      enableDraw: vi.fn(async () => {}),
    }
    setGeoman(gm)
    await buildDrawControl(polygonPhase())
    expect(gm.disableDraw).toHaveBeenCalled()
    expect(mockDelay).toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(0)
    expect(gm.enableDraw).toHaveBeenCalledWith("polygon")
  })

  it("logs an error when disableDraw fails", async () => {
    const gm = {
      getActiveDrawModes: vi.fn(() => ["line"]),
      disableDraw: vi.fn(async () => {
        throw new Error("disable boom")
      }),
      enableDraw: vi.fn(async () => {}),
    }
    setGeoman(gm)
    await buildDrawControl(polygonPhase())
    await vi.advanceTimersByTimeAsync(0)
    expect(mockDebugError).toHaveBeenCalled()
  })

  it("logs an error when enableDraw rejects", async () => {
    const gm = {
      getActiveDrawModes: vi.fn(() => []),
      disableDraw: vi.fn(async () => {}),
      enableDraw: vi.fn(async () => {
        throw new Error("enable boom")
      }),
    }
    setGeoman(gm)
    await buildDrawControl(polygonPhase())
    await vi.advanceTimersByTimeAsync(0)
    expect(mockDebugError).toHaveBeenCalled()
  })

  it("sets up an edge-visibility poll for polygon shapes", async () => {
    const gm = {
      getActiveDrawModes: vi.fn(() => []),
      disableDraw: vi.fn(async () => {}),
      enableDraw: vi.fn(async () => {}),
    }
    setGeoman(gm)
    await buildDrawControl(polygonPhase())
    await vi.advanceTimersByTimeAsync(0)
    expect(mockEnsureGeomanDrawEdgesVisible).toHaveBeenCalled()
    const store = useDrawStore()
    expect(store.edgePollId).not.toBeNull()
    expect(store.edgeTimeoutId).not.toBeNull()

    // Advance past the retry interval to exercise the poll callback
    const before = mockEnsureGeomanDrawEdgesVisible.mock.calls.length
    await vi.advanceTimersByTimeAsync(200)
    expect(mockEnsureGeomanDrawEdgesVisible.mock.calls.length).toBeGreaterThan(before)

    // After the timeout the poll is cleared
    await vi.advanceTimersByTimeAsync(2500)
    expect(store.edgePollId).toBeNull()
    expect(store.edgeTimeoutId).toBeNull()
  })
})
