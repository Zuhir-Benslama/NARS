// ─── STORE TESTS ──────────────────────────────────────────────────────────────
// Tests for Pinia stores.

import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import {
  useAppStore,
  useModalStore,
  useLayerStore,
  awaitModalResult,
  setCurrentModalFeatureId,
} from "../stores"
import * as modalStoreModule from "../stores/modalStore"
import type { FeatureData, RoadOption } from "../types"

describe("Pinia stores", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  describe("useAppStore", () => {
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

  describe("useModalStore", () => {
    it("initializes with default state", () => {
      const modalStore = useModalStore()
      expect(modalStore.visible).toBe(false)
      expect(modalStore.isEdit).toBe(false)
      expect(modalStore.phaseIndex).toBeNull()
    })

    it("opens create modal and resolves result", async () => {
      const modalStore = useModalStore()
      modalStore.openCreate(0)

      expect(modalStore.visible).toBe(true)
      expect(modalStore.phaseIndex).toBe(0)
      expect(modalStore.isEdit).toBe(false)

      const promise = awaitModalResult()
      modalStore.close({ label: "Test", decisionNumber: "123", decisionDate: "2024-01-01" })
      const result = await promise
      expect(result).toEqual({
        label: "Test",
        decisionNumber: "123",
        decisionDate: "2024-01-01",
      })
    })

    it("opens edit modal with existing data", async () => {
      const modalStore = useModalStore()
      const existingData: FeatureData = {
        type: "areas",
        label: "Existing Area",
        decisionNumber: "2023/001",
        decisionDate: "2023-06-15",
        areaTypeKey: "central_urban",
      }

      modalStore.openEdit(0, "test-uuid-123", existingData)

      expect(modalStore.visible).toBe(true)
      expect(modalStore.isEdit).toBe(true)
      expect(modalStore.editDbId).toBe("test-uuid-123")
      expect(modalStore.label).toBe("Existing Area")
      expect(modalStore.decisionNumber).toBe("2023/001")

      const promise = awaitModalResult()
      modalStore.close({ label: "Updated", decisionNumber: "2024/002", decisionDate: "2024-01-01" })
      const result = await promise
      expect(result?.label).toBe("Updated")
    })

    it("handles cancel (null result)", async () => {
      const modalStore = useModalStore()
      modalStore.openCreate(0)
      const promise = awaitModalResult()
      modalStore.close(null)
      const result = await promise
      expect(result).toBeNull()
    })

    it("sets road options", () => {
      const modalStore = useModalStore()
      const options: RoadOption[] = [
        { idx: 1, label: "Road 1", dbId: "db-1" },
        { idx: 2, label: "Road 2", dbId: "db-2" },
      ]
      modalStore.setRoadOptions(options)
      expect(modalStore.roadOptions).toEqual(options)
      expect(modalStore.selectedRoadIdx).toBe("")
    })
  })

  describe("useLayerStore", () => {
    it("initializes with empty arrays", () => {
      const layerStore = useLayerStore()
      expect(layerStore.areas).toEqual([])
      expect(layerStore.districts).toEqual([])
      expect(layerStore.roads).toEqual([])
      expect(layerStore.houseEntrances).toEqual([])
      expect(layerStore.publicBuildings).toEqual([])
      expect(layerStore.publicSpaces).toEqual([])
    })

    it("adds features to layers", () => {
      const layerStore = useLayerStore()
      layerStore.addFeature("areas", {
        id: "1",
        dbId: "uuid-1",
        data: { type: "areas" } as FeatureData,
        type: "polygon",
      })
      expect(layerStore.areas).toHaveLength(1)
      expect(layerStore.areaCount).toBe(1)
    })

    it("removes features from layers", () => {
      const layerStore = useLayerStore()
      layerStore.addFeature("roads", {
        id: "1",
        dbId: "uuid-1",
        data: { type: "roads" } as FeatureData,
        type: "line",
      })
      layerStore.addFeature("roads", {
        id: "2",
        dbId: "uuid-2",
        data: { type: "roads" } as FeatureData,
        type: "line",
      })
      layerStore.removeFeature("roads", "uuid-1")
      expect(layerStore.roads).toHaveLength(1)
      expect(layerStore.roads[0].dbId).toBe("uuid-2")
    })

    it("updates feature data", () => {
      const layerStore = useLayerStore()
      layerStore.addFeature("areas", {
        id: "1",
        dbId: "uuid-1",
        data: { type: "areas", label: "Original" } as FeatureData,
        type: "polygon",
      })
      layerStore.updateFeature("areas", "uuid-1", { label: "Updated" })
      expect(layerStore.areas[0].data.label).toBe("Updated")
    })

    it("finds feature by dbId", () => {
      const layerStore = useLayerStore()
      layerStore.addFeature("roads", {
        id: "1",
        dbId: "uuid-1",
        data: { type: "roads" } as FeatureData,
        type: "line",
      })
      const found = layerStore.getFeature("uuid-1")
      expect(found).not.toBeNull()
      expect(found?.dbId).toBe("uuid-1")
    })

    it("clears a layer", () => {
      const layerStore = useLayerStore()
      layerStore.addFeature("areas", {
        id: "1",
        dbId: "uuid-1",
        data: { type: "areas" } as FeatureData,
        type: "polygon",
      })
      layerStore.clearLayer("areas")
      expect(layerStore.areas).toEqual([])
    })

    it("computes main/secondary entrance counts", () => {
      const layerStore = useLayerStore()
      layerStore.addFeature("houseEntrances", {
        id: "1",
        dbId: "uuid-1",
        data: { type: "houseEntrances", entranceTypeKey: "main_entrance" } as FeatureData,
        type: "marker",
      })
      layerStore.addFeature("houseEntrances", {
        id: "2",
        dbId: "uuid-2",
        data: { type: "houseEntrances", entranceTypeKey: "secondary_entrance" } as FeatureData,
        type: "marker",
      })
      layerStore.addFeature("houseEntrances", {
        id: "3",
        dbId: "uuid-3",
        data: { type: "houseEntrances", entranceTypeKey: "main_entrance" } as FeatureData,
        type: "marker",
      })
      expect(layerStore.mainEntranceCount).toBe(2)
      expect(layerStore.secondaryEntranceCount).toBe(1)
    })

    it("resets to initial state", () => {
      const layerStore = useLayerStore()
      layerStore.addFeature("areas", {
        id: "1",
        dbId: "uuid-1",
        data: { type: "areas" } as FeatureData,
        type: "polygon",
      })
      layerStore.reset()
      expect(layerStore.areas).toEqual([])
    })
  })

  describe("modal promise bridge", () => {
    it("setCurrentModalFeatureId updates the feature id", () => {
      setCurrentModalFeatureId("test-id")
      expect(modalStoreModule.currentModalFeatureId).toBe("test-id")
    })
  })
})
