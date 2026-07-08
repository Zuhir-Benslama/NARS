import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

let useSnapStore: any

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  const mod = await import("./snapStore")
  useSnapStore = mod.useSnapStore as any
})

describe("snapStore", () => {
  it("initializes with default state", () => {
    const store = useSnapStore()
    expect(store.crosshairActive).toBe(false)
    expect(store.snapActive).toBe(false)
    expect(store.snapLatLng).toBeNull()
    expect(store.snapFrozen).toBe(false)
    expect(store.snapRafId).toBeNull()
    expect(store.snapPendingEvent).toBeNull()
    expect(store.editModeActive).toBe(false)
    expect(store.editDragActive).toBe(false)
    expect(store.snapExclude).toBeNull()
  })

  it("enableCrosshair sets crosshairActive", () => {
    const store = useSnapStore()
    store.enableCrosshair()
    expect(store.crosshairActive).toBe(true)
  })

  it("enableCrosshair is idempotent", () => {
    const store = useSnapStore()
    store.enableCrosshair()
    store.enableCrosshair()
    expect(store.crosshairActive).toBe(true)
  })

  it("disableCrosshair clears crosshairActive", () => {
    const store = useSnapStore()
    store.enableCrosshair()
    store.disableCrosshair()
    expect(store.crosshairActive).toBe(false)
  })

  it("setEditModeActive updates state", () => {
    const store = useSnapStore()
    store.setEditModeActive(true)
    expect(store.editModeActive).toBe(true)
  })

  it("setEditDragActive updates state", () => {
    const store = useSnapStore()
    store.setEditDragActive(true)
    expect(store.editDragActive).toBe(true)
  })

  it("clearPendingEvent resets snapPendingEvent", () => {
    const store = useSnapStore()
    store.snapPendingEvent = {} as any
    store.clearPendingEvent()
    expect(store.snapPendingEvent).toBeNull()
  })

  it("resetSnap resets to initial state", () => {
    const store = useSnapStore()
    store.crosshairActive = true
    store.snapActive = true
    store.snapFrozen = true
    store.resetSnap()
    expect(store.crosshairActive).toBe(false)
    expect(store.snapActive).toBe(false)
    expect(store.snapFrozen).toBe(false)
  })

  it("getFrozenSnapPos returns null when not frozen", () => {
    const store = useSnapStore()
    expect(store.getFrozenSnapPos).toBeNull()
  })

  it("getFrozenSnapPos returns latLng when frozen", () => {
    const store = useSnapStore()
    store.snapFrozen = true
    store.snapLatLng = { lat: 36.0, lng: 127.0 }
    expect(store.getFrozenSnapPos).toEqual({ lat: 36.0, lng: 127.0 })
  })

  it("getFrozenSnapPos returns null when frozen but no latLng", () => {
    const store = useSnapStore()
    store.snapFrozen = true
    expect(store.getFrozenSnapPos).toBeNull()
  })
})
