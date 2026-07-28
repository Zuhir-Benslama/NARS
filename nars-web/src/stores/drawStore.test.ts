import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

let useDrawStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  const mod = await import("./drawStore")
  useDrawStore = mod.useDrawStore as any
})

describe("drawStore", () => {
  it("initializes with default state", () => {
    const store = useDrawStore()
    expect(store.snappingEnabled).toBe(true)
    expect(store.geomanMarkerPointer).toBeNull()
    expect(store.originalGeomanMarkerSetLngLat).toBeNull()
    expect(store.drawingPhase).toBeNull()
    expect(store.savingFeature).toBe(false)
    expect(store.modeSwitchToken).toBe(0)
  })

  it("registerGeomanMarker stores references", () => {
    const store = useDrawStore()
    const orig = vi.fn()
    const marker = { setLngLat: vi.fn() }
    store.registerGeomanMarker({ marker }, null, orig)
    expect(store.originalGeomanMarkerSetLngLat).toBeDefined()
    expect(store.geomanMarkerPointer?.marker?.setLngLat).toBeDefined()
  })

  it("unpatchGeomanMarker restores original setLngLat", () => {
    const store = useDrawStore()
    const orig = vi.fn()
    const marker = { setLngLat: vi.fn() }
    store.registerGeomanMarker({ marker }, null, orig)
    store.unpatchGeomanMarker()
    expect(marker.setLngLat).toBe(orig)
    expect(store.snappingEnabled).toBe(false)
  })

  it("unpatchGeomanMarker does not throw when marker is null", () => {
    const store = useDrawStore()
    store.registerGeomanMarker(null as any, null, null)
    expect(() => store.unpatchGeomanMarker()).not.toThrow()
    expect(store.snappingEnabled).toBe(false)
  })

  it("setSnappingEnabled updates flag", () => {
    const store = useDrawStore()
    store.setSnappingEnabled(false)
    expect(store.snappingEnabled).toBe(false)
  })

  it("setRepatchMarkerPointer stores callback", () => {
    const store = useDrawStore()
    const fn = vi.fn()
    store.setRepatchMarkerPointer(fn)
    expect(store.repatchMarkerPointer).toBe(fn)
  })

  it("repatchMarker calls the stored callback", () => {
    const store = useDrawStore()
    const fn = vi.fn()
    store.setRepatchMarkerPointer(fn)
    store.repatchMarker()
    expect(fn).toHaveBeenCalled()
  })

  it("setDrawingPhase stores phase", () => {
    const store = useDrawStore()
    const phase = { key: "roads", label: "Roads" } as any
    store.setDrawingPhase(phase)
    expect(store.drawingPhase.key).toBe(phase.key)
    expect(store.drawingPhase.label).toBe(phase.label)
  })

  it("setSavingFeature updates flag", () => {
    const store = useDrawStore()
    store.setSavingFeature(true)
    expect(store.savingFeature).toBe(true)
  })

  it("incrementModeSwitchToken increments and returns", () => {
    const store = useDrawStore()
    expect(store.incrementModeSwitchToken()).toBe(1)
    expect(store.incrementModeSwitchToken()).toBe(2)
  })

  it("resetDraw resets to initial state", () => {
    const store = useDrawStore()
    store.setSavingFeature(true)
    store.resetDraw()
    expect(store.savingFeature).toBe(false)
    expect(store.modeSwitchToken).toBe(0)
  })
})
