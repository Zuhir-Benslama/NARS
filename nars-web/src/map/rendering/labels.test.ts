import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { _setCtx, resetMapState } from "../core/state"
import { useAppStore } from "../../stores/appStore"
import { refreshLayerVisibility } from "./labels"

function makeMockMap() {
  return {
    getLayer: vi.fn(() => true),
    setFilter: vi.fn(),
    setLayoutProperty: vi.fn(),
  }
}

describe("labels", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetMapState()
    _setCtx({ map: makeMockMap() as any })
  })

  describe("refreshLayerVisibility", () => {
    it("calls setFilter on known layers", () => {
      const appStore = useAppStore()
      appStore.currentPhase = 0
      refreshLayerVisibility()
    })

    it("handles map not being initialized", () => {
      resetMapState()
      _setCtx({} as any)
      expect(() => refreshLayerVisibility()).not.toThrow()
    })

    it("shows endpoint layers during roads phase", () => {
      const map = makeMockMap()
      resetMapState()
      _setCtx({ map: map as any })
      const appStore = useAppStore()
      appStore.currentPhase = 4
      refreshLayerVisibility()
      expect(map.setLayoutProperty).toHaveBeenCalled()
    })
  })
})
