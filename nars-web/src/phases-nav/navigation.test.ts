import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import type { LayerEntry } from "../types/features"

function makeEntry(overrides: Partial<LayerEntry> = {}): LayerEntry {
  return {
    id: "id-1",
    dbId: "db-1",
    type: "polygon",
    data: {
      type: "areas",
      label: "Test",
      decisionNumber: "",
      decisionDate: "",
      areaTypeKey: "central_urban",
    },
    ...overrides,
  }
}

vi.mock("../lib/validation", () => ({
  checkDistrictCoverage: vi.fn().mockResolvedValue({ covered: true, message: "" }),
}))

vi.mock("../map/draw/draw-complete", () => ({
  setDrawingPhase: vi.fn(),
}))

vi.mock("../map/draw/draw-control", () => ({
  buildDrawControl: vi.fn(),
}))

vi.mock("../map/rendering/labels", () => ({
  refreshLayerVisibility: vi.fn(),
}))

vi.mock("../map/roads/road-directions", () => ({
  computeAndApplyRoadDirections: vi.fn(),
}))

vi.mock("./storage", () => ({
  savePhase: vi.fn(),
}))

vi.mock("../lib/toast", () => ({
  showToast: vi.fn(),
}))

describe("setPhase", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("updates currentPhase in appStore", async () => {
    const { setPhase } = await import("./navigation")
    const appStore = useAppStore()
    setPhase(2)
    expect(appStore.currentPhase).toBe(2)
  })

  it("saves phase to storage", async () => {
    const { setPhase } = await import("./navigation")
    const { savePhase } = await import("./storage")
    const appStore = useAppStore()
    appStore.setUser({
      id: 1,
      username: "u",
      name: "U",
      email: "u@u.com",
      role: "commune_user",
      commune: { id: 99, name_fr: "C", name_ar: "", latitude: null, longitude: null },
    })
    setPhase(1)
    expect(savePhase).toHaveBeenCalledWith(1, 99)
  })
})

describe("navigatePhase", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("does nothing when target is out of bounds (backward)", async () => {
    const { navigatePhase } = await import("./navigation")
    const appStore = useAppStore()
    appStore.currentPhase = 0
    await navigatePhase(-1)
    expect(appStore.currentPhase).toBe(0)
  })

  it("does nothing when target is out of bounds (forward)", async () => {
    const { navigatePhase } = await import("./navigation")
    const appStore = useAppStore()
    const { PHASES } = await import("../phases")
    appStore.currentPhase = PHASES.length - 1
    await navigatePhase(1)
    expect(appStore.currentPhase).toBe(PHASES.length - 1)
  })

  it("blocks forward navigation when areas layer is empty", async () => {
    const { navigatePhase } = await import("./navigation")
    const appStore = useAppStore()
    appStore.currentPhase = 0
    await navigatePhase(1)
    expect(appStore.currentPhase).toBe(0)
  })

  it("navigates forward when areas are present", async () => {
    const { navigatePhase } = await import("./navigation")
    const appStore = useAppStore()
    const layerStore = useLayerStore()
    layerStore.addFeature("areas", makeEntry())
    appStore.currentPhase = 0
    await navigatePhase(1)
    expect(appStore.currentPhase).toBe(1)
  })

  it("blocks forward navigation from roads when roads layer is empty", async () => {
    const { navigatePhase } = await import("./navigation")
    const appStore = useAppStore()
    const layerStore = useLayerStore()
    layerStore.addFeature("areas", makeEntry())
    layerStore.addFeature("districts", makeEntry())
    layerStore.addFeature("cityCenter", makeEntry())
    appStore.currentPhase = 0
    await navigatePhase(1)
    await navigatePhase(1)
    await navigatePhase(1)
    await navigatePhase(1)
    expect(appStore.currentPhase).toBe(3)
  })

  it("auto-orients road directions when leaving roads with roads present", async () => {
    const { navigatePhase } = await import("./navigation")
    const { computeAndApplyRoadDirections } = await import("../map/roads/road-directions")
    const appStore = useAppStore()
    const layerStore = useLayerStore()
    layerStore.addFeature("areas", makeEntry())
    layerStore.addFeature("districts", makeEntry())
    layerStore.addFeature("cityCenter", makeEntry())
    layerStore.addFeature("roads", makeEntry({ id: "road-1", type: "line" }))
    appStore.currentPhase = 3
    await navigatePhase(1)
    expect(computeAndApplyRoadDirections).toHaveBeenCalled()
    expect(appStore.currentPhase).toBe(4)
  })

  it("blocks forward navigation from districts when coverage is incomplete", async () => {
    const { navigatePhase } = await import("./navigation")
    const { checkDistrictCoverage } = await import("../lib/validation")
    vi.mocked(checkDistrictCoverage).mockResolvedValueOnce({
      covered: false,
      message: "missing areas",
    })
    const appStore = useAppStore()
    const layerStore = useLayerStore()
    layerStore.addFeature("areas", makeEntry())
    appStore.currentPhase = 0
    await navigatePhase(1)
    expect(appStore.currentPhase).toBe(1)
    await navigatePhase(1)
    expect(appStore.currentPhase).toBe(1)
  })

  it("blocks forward navigation from houseEntrances when entrances layer is empty", async () => {
    const { navigatePhase } = await import("./navigation")
    const { showToast } = await import("../lib/toast")
    const appStore = useAppStore()
    const layerStore = useLayerStore()
    layerStore.addFeature("areas", makeEntry())
    layerStore.addFeature("districts", makeEntry())
    layerStore.addFeature("cityCenter", makeEntry())
    layerStore.addFeature("roads", makeEntry({ id: "road-1", type: "line" }))
    appStore.currentPhase = 3
    await navigatePhase(1)
    await navigatePhase(1)
    expect(appStore.currentPhase).toBe(4)
    expect(showToast).toHaveBeenCalledWith("alert_at_least_one_entrance", "error")
  })
})

describe("goToPhase", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("does nothing when target equals current phase", async () => {
    const { goToPhase } = await import("./navigation")
    const appStore = useAppStore()
    appStore.currentPhase = 3
    await goToPhase(3)
    expect(appStore.currentPhase).toBe(3)
  })

  it("jumps to target when going backward", async () => {
    const { goToPhase } = await import("./navigation")
    const appStore = useAppStore()
    appStore.currentPhase = 5
    await goToPhase(1)
    expect(appStore.currentPhase).toBe(1)
  })

  it("navigates forward through intermediate phases", async () => {
    const { goToPhase } = await import("./navigation")
    const appStore = useAppStore()
    const layerStore = useLayerStore()
    layerStore.addFeature("areas", makeEntry())
    layerStore.addFeature("districts", makeEntry())
    layerStore.addFeature("cityCenter", makeEntry())
    appStore.currentPhase = 0
    await goToPhase(3)
    expect(appStore.currentPhase).toBe(3)
  })

  it("aborts forward navigation when an intermediate phase is blocked", async () => {
    const { goToPhase } = await import("./navigation")
    const appStore = useAppStore()
    appStore.currentPhase = 0
    await goToPhase(3)
    expect(appStore.currentPhase).toBe(0)
  })
})
