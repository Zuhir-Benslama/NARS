import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { nextTick } from "vue"

const {
  mockSetDrawingPhase,
  mockBuildDrawControl,
  mockCancelPendingDrawModeSwitch,
  mockDisableCrosshair,
  mockResetSnapping,
} = vi.hoisted(() => ({
  mockSetDrawingPhase: vi.fn(),
  mockBuildDrawControl: vi.fn(),
  mockCancelPendingDrawModeSwitch: vi.fn(),
  mockDisableCrosshair: vi.fn(),
  mockResetSnapping: vi.fn(),
}))

const { mockGetActiveDrawModes, mockFlyTo } = vi.hoisted(() => ({
  mockGetActiveDrawModes: vi.fn((): string[] => []),
  mockFlyTo: vi.fn(),
}))

vi.mock("../../utils/debug", () => ({
  debugWarn: vi.fn(),
}))

vi.mock("../core/state", () => ({
  getCtx: () => ({
    geoman: { getActiveDrawModes: mockGetActiveDrawModes },
    map: { flyTo: mockFlyTo },
  }),
}))

vi.mock("../draw/draw-state", () => ({
  setRepatchMarkerPointer: vi.fn(),
  setDrawingPhase: mockSetDrawingPhase,
}))

vi.mock("./draw-control", () => ({
  buildDrawControl: mockBuildDrawControl,
  cancelPendingDrawModeSwitch: mockCancelPendingDrawModeSwitch,
}))

vi.mock("../draw/draw-marker-patch", () => ({
  repatchMarkerPointer: vi.fn(),
  patchGeomanMarkerPointerSnap: vi.fn(),
}))

vi.mock("../draw/draw-handlers", () => ({
  registerDrawHandlers: vi.fn(),
  destroyDrawHandlers: vi.fn(),
}))

vi.mock("../snapping/snapping", () => ({
  disableCrosshair: mockDisableCrosshair,
  resetSnapping: mockResetSnapping,
  disableSnapping: vi.fn(),
  installSnapInterceptors: vi.fn(),
  uninstallSnapInterceptors: vi.fn(),
}))

vi.mock("../edit/edit-mode", () => ({
  isEditMode: () => false,
}))

let registerDrawEvents: () => void
let destroyDrawEvents: () => void
let useDrawStore: any
let useAppStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  mockGetActiveDrawModes.mockReturnValue([])
  setActivePinia(createPinia())
  const drawEvents = await import("./draw-events")
  registerDrawEvents = drawEvents.registerDrawEvents
  destroyDrawEvents = drawEvents.destroyDrawEvents
  const drawStore = await import("../../stores/drawStore")
  useDrawStore = drawStore.useDrawStore
  const appStore = await import("../../stores/appStore")
  useAppStore = appStore.useAppStore
})

describe("registerDrawEvents watcher lifecycle", () => {
  it("creates a draw-phase watcher on registration", () => {
    registerDrawEvents()
    const store = useDrawStore()
    expect(store.cleanupDrawWatcher).toBeTypeOf("function")
    expect(mockSetDrawingPhase).toHaveBeenCalledTimes(1) // immediate: true
  })

  it("stops the previous watcher when re-registered (no watcher leak)", async () => {
    registerDrawEvents()
    const appStore = useAppStore()

    // Watch counter: the phase watcher fires on change. If the first watcher
    // leaked, the change would fire its callback too.
    mockSetDrawingPhase.mockClear()
    mockDisableCrosshair.mockClear()
    mockResetSnapping.mockClear()

    registerDrawEvents()
    mockSetDrawingPhase.mockClear()
    mockDisableCrosshair.mockClear()
    mockResetSnapping.mockClear()

    appStore.currentPhase = 2
    await nextTick()

    // Exactly one watcher alive → each callback runs exactly once.
    expect(mockSetDrawingPhase).toHaveBeenCalledTimes(1)
    expect(mockDisableCrosshair).toHaveBeenCalledTimes(1)
    expect(mockResetSnapping).toHaveBeenCalledTimes(1)
  })

  it("destroyDrawEvents clears the watcher reference", () => {
    registerDrawEvents()
    const store = useDrawStore()
    expect(store.cleanupDrawWatcher).toBeTypeOf("function")

    destroyDrawEvents()
    expect(store.cleanupDrawWatcher).toBeNull()
  })

  it("re-arms the new phase's draw tool when a draw mode was active", async () => {
    registerDrawEvents()
    const appStore = useAppStore()

    // User was drawing a line (roads phase) and switches to markers.
    mockGetActiveDrawModes.mockReturnValue(["line"])
    mockBuildDrawControl.mockClear()
    mockDisableCrosshair.mockClear()
    mockResetSnapping.mockClear()

    appStore.currentPhase = 4 // houseEntrances — marker
    await nextTick()

    expect(mockBuildDrawControl).toHaveBeenCalledTimes(1)
    expect(mockBuildDrawControl).toHaveBeenCalledWith(
      expect.objectContaining({ key: "houseEntrances", drawType: "marker" }),
    )
    // An active draw tool keeps its crosshair cursor across phase changes.
    expect(mockDisableCrosshair).not.toHaveBeenCalled()
    expect(mockResetSnapping).toHaveBeenCalledTimes(1)
  })

  it("does not arm a draw tool when switching phases in hand mode", async () => {
    registerDrawEvents()
    const appStore = useAppStore()

    mockBuildDrawControl.mockClear()
    mockCancelPendingDrawModeSwitch.mockClear()
    mockDisableCrosshair.mockClear()
    mockResetSnapping.mockClear()

    appStore.currentPhase = 3 // roads — polyline
    await nextTick()

    expect(mockBuildDrawControl).not.toHaveBeenCalled()
    // Even in hand mode, a stale in-flight switch from the old phase is cancelled.
    expect(mockCancelPendingDrawModeSwitch).toHaveBeenCalledTimes(1)
    expect(mockDisableCrosshair).toHaveBeenCalledTimes(1)
  })
})
