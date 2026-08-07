import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useAppStore } from "./appStore"
import { useLayerStore } from "./layerStore"

describe("useAppStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("initializes with default state", () => {
    const appStore = useAppStore()
    expect(appStore.currentPhase).toBe(0)
    expect(appStore.user).toBeNull()
    expect(appStore.loadError).toBe(false)
    expect(appStore.isLoading).toBe(false)
    expect(appStore.isAuthenticated).toBe(false)
    expect(appStore.isAdminUser).toBe(false)
  })

  it("sets user and updates computed properties", () => {
    const appStore = useAppStore()
    appStore.setUser({
      id: 1,
      username: "testuser",
      name: "Test User",
      email: "test@example.com",
      role: "commune_user",
      commune: {
        id: 1,
        name_fr: "Test Commune",
        name_ar: "بلدية اختبار",
        latitude: null,
        longitude: null,
      },
    })
    expect(appStore.isAuthenticated).toBe(true)
    expect(appStore.isAdminUser).toBe(false)
    expect(appStore.communeName).toBe("Test Commune")
  })

  it("detects admin users", () => {
    const appStore = useAppStore()
    appStore.setUser({
      id: 1,
      username: "admin",
      name: "Admin User",
      email: "admin@example.com",
      role: "wilaya_admin",
      commune: { id: null, name_fr: null, name_ar: null, latitude: null, longitude: null },
    })
    expect(appStore.isAdminUser).toBe(true)
  })

  it("derives counts from the layer store", () => {
    const appStore = useAppStore()
    const layerStore = useLayerStore()
    layerStore.addFeature("areas", {
      id: "a1",
      dbId: "a1",
      type: "polygon",
      data: {
        type: "areas",
        label: "A1",
        decisionNumber: "",
        decisionDate: "",
        areaTypeKey: "central_urban",
      },
    } as never)
    layerStore.addFeature("roads", {
      id: "r1",
      dbId: "r1",
      type: "line",
      data: {
        type: "roads",
        label: "R1",
        decisionNumber: "",
        decisionDate: "",
        roadTypeKey: "street",
      },
    } as never)
    layerStore.addFeature("houseEntrances", {
      id: "e1",
      dbId: "e1",
      type: "marker",
      data: { type: "houseEntrances", label: "E1", entranceTypeKey: "main_entrance" },
    } as never)

    expect(appStore.counts.areas).toBe(1)
    expect(appStore.counts.roads).toBe(1)
    expect(appStore.counts.mainEntrances).toBe(1)
    expect(appStore.counts.secondaryEntrances).toBe(0)
  })

  it("resets to initial state", () => {
    const appStore = useAppStore()
    appStore.setLoading(true)
    appStore.setLoadError(true)
    appStore.$reset()
    expect(appStore.isLoading).toBe(false)
    expect(appStore.loadError).toBe(false)
  })
})
