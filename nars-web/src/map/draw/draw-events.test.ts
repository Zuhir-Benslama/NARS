import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { nextTick } from "vue"

const { mockBuildDrawControl, mockSetDrawingPhase, mockEnableCrosshair, mockResetSnapping } =
  vi.hoisted(() => ({
    mockBuildDrawControl: vi.fn(),
    mockSetDrawingPhase: vi.fn(),
    mockEnableCrosshair: vi.fn(),
    mockResetSnapping: vi.fn(),
  }))

const { mockGetActiveDrawModes, mockFlyTo } = vi.hoisted(() => ({
  mockGetActiveDrawModes: vi.fn(() => []),
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

vi.mock("../draw/draw-marker-patch", () => ({
  repatchMarkerPointer: vi.fn(),
  patchGeomanMarkerPointerSnap: vi.fn(),
}))

vi.mock("../draw/draw-handlers", () => ({
  registerDrawHandlers: vi.fn(),
  destroyDrawHandlers: vi.fn(),
}))

vi.mock("../draw/draw-control", () => ({
  buildDrawControl: mockBuildDrawControl,
}))

vi.mock("../snapping/snapping", () => ({
  enableCrosshair: mockEnableCrosshair,
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
    expect(mockBuildDrawControl).toHaveBeenCalledTimes(1) // immediate: true
  })

  it("stops the previous watcher when re-registered (no watcher leak)", async () => {
    registerDrawEvents()
    const appStore = useAppStore()

    // Watch counter: the phase watcher fires on change. If the first watcher
    // leaked, the change would fire its callback too.
    mockBuildDrawControl.mockClear()
    mockSetDrawingPhase.mockClear()
    mockEnableCrosshair.mockClear()
    mockResetSnapping.mockClear()

    registerDrawEvents()
    mockBuildDrawControl.mockClear()
    mockSetDrawingPhase.mockClear()
    mockEnableCrosshair.mockClear()
    mockResetSnapping.mockClear()

    appStore.currentPhase = 1
    await nextTick()

    // Exactly one watcher alive → each callback runs exactly once.
    expect(mockBuildDrawControl).toHaveBeenCalledTimes(1)
    expect(mockSetDrawingPhase).toHaveBeenCalledTimes(1)
    expect(mockEnableCrosshair).toHaveBeenCalledTimes(1)
    expect(mockResetSnapping).toHaveBeenCalledTimes(1)
  })

  it("destroyDrawEvents clears the watcher reference", () => {
    registerDrawEvents()
    const store = useDrawStore()
    expect(store.cleanupDrawWatcher).toBeTypeOf("function")

    destroyDrawEvents()
    expect(store.cleanupDrawWatcher).toBeNull()
  })
})
