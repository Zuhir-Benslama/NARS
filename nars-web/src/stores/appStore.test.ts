import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useAppStore } from "./appStore"

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
    expect(appStore.municipalityName).toBe("Test Commune")
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

  it("updates counts", () => {
    const appStore = useAppStore()
    appStore.updateCounts({
      areas: 5,
      cityCenter: 1,
      districts: 3,
      roads: 10,
      mainEntrances: 8,
      secondaryEntrances: 4,
      publicBuildings: 2,
      publicSpaces: 1,
      namingPanels: 6,
    })
    expect(appStore.counts.areas).toBe(5)
    expect(appStore.counts.roads).toBe(10)
  })

  it("resets to initial state", () => {
    const appStore = useAppStore()
    appStore.setLoading(true)
    appStore.setLoadError(true)
    appStore.reset()
    expect(appStore.isLoading).toBe(false)
    expect(appStore.loadError).toBe(false)
  })
})
