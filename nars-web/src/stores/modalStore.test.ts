import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import {
  useModalStore,
  awaitModalResult,
  openModal,
  openEditModal,
  resolveModal,
  resetModalBridge,
} from "./modalStore"
import type { ModalResult } from "../types/modal"

const emptyResult: ModalResult = {
  type: "namingPanels",
  label: "",
  decisionNumber: "",
  decisionDate: "",
}

describe("modalStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  describe("store actions", () => {
    it("openCreate sets visible and phaseIndex", () => {
      const store = useModalStore()
      store.openCreate(2)
      expect(store.visible).toBe(true)
      expect(store.phaseIndex).toBe(2)
      expect(store.isEdit).toBe(false)
      expect(store.editDbId).toBeNull()
    })

    it("openCreate preserves radius extras", () => {
      const store = useModalStore()
      store.openCreate(1, { radius: 500 })
      expect(store.radius).toBe(500)
    })

    it("openEdit sets edit mode with existing data", () => {
      const store = useModalStore()
      store.openEdit(0, "db-123", {
        type: "areas",
        label: "Edit Me",
        decisionNumber: "DN-01",
        decisionDate: "2024-01-01",
        areaTypeKey: "peri_urban",
      } as any)
      expect(store.visible).toBe(true)
      expect(store.isEdit).toBe(true)
      expect(store.editDbId).toBe("db-123")
      expect(store.label).toBe("Edit Me")
      expect(store.areaTypeKey).toBe("peri_urban")
    })

    it("openEdit defaults entranceTypeKey to main_entrance when a road is assigned", () => {
      const store = useModalStore()
      store.openEdit(0, "db-123", {
        type: "houseEntrances",
        label: "Entrance",
        roadDbId: "road-1",
      } as any)
      expect(store.entranceTypeKey).toBe("main_entrance")
    })

    it("openEdit defaults entranceTypeKey to secondary_entrance without a road", () => {
      const store = useModalStore()
      store.openEdit(0, "db-123", {
        type: "houseEntrances",
        label: "Entrance",
      } as any)
      expect(store.entranceTypeKey).toBe("secondary_entrance")
    })

    it("openEdit preserves entrance-side and numbering fields", () => {
      const store = useModalStore()
      store.openEdit(0, "db-123", {
        type: "houseEntrances",
        label: "Entrance",
        side: "right",
        entranceNumber: 12,
        bisNumber: 3,
        radius: 500,
        spaceTypeKey: "square",
        sectorKey: "commerce",
        buildingTypeKey: "store",
        entranceTypeKey: "secondary_entrance",
      } as any)
      expect(store.entranceSide).toBe("right")
      expect(store.entranceNumber).toBe(12)
      expect(store.bisNumber).toBe(3)
      expect(store.radius).toBe(500)
      expect(store.spaceTypeKey).toBe("square")
      expect(store.sectorKey).toBe("commerce")
      expect(store.buildingTypeKey).toBe("store")
      expect(store.entranceTypeKey).toBe("secondary_entrance")
    })

    it("close hides the modal", () => {
      const store = useModalStore()
      store.openCreate(0)
      store.close()
      expect(store.visible).toBe(false)
    })

    it("resetFields restores defaults", () => {
      const store = useModalStore()
      store.openCreate(0)
      store.label = "Custom"
      store.resetFields()
      expect(store.label).toBe("")
      expect(store.visible).toBe(false)
    })

    it("setRoadOptions sets options and resets selection", () => {
      const store = useModalStore()
      store.setRoadOptions([{ idx: 0, dbId: "r1", label: "Road 1" }])
      expect(store.roadOptions).toHaveLength(1)
      expect(store.selectedRoadIdx).toBe("")
    })

    it("setMainEntranceOptions sets options and resets selection", () => {
      const store = useModalStore()
      store.setMainEntranceOptions([{ idx: 0, dbId: "m1", label: "Main 1" }])
      expect(store.mainEntranceOptions).toHaveLength(1)
      expect(store.selectedMainIdx).toBe("")
    })
  })

  describe("promise queue", () => {
    it("awaitModalResult resolves when close is called", async () => {
      const store = useModalStore()
      const promise = awaitModalResult()
      store.close({ type: "namingPanels", label: "Done", decisionNumber: "", decisionDate: "" })
      await expect(promise).resolves.toEqual({
        type: "namingPanels",
        label: "Done",
        decisionNumber: "",
        decisionDate: "",
      })
    })

    it("awaitModalResult resolves to null when close is called without result", async () => {
      const store = useModalStore()
      const promise = awaitModalResult()
      store.close()
      await expect(promise).resolves.toBeNull()
    })

    it("second awaitModalResult replaces the first pending promise", async () => {
      const store = useModalStore()
      awaitModalResult() // first is replaced
      const secondPromise = awaitModalResult()
      store.close(emptyResult)
      await expect(secondPromise).resolves.toEqual(emptyResult)
    })

    it("awaitModalResult resolves immediately with a result cached while no resolver was pending", async () => {
      const store = useModalStore()
      store.close(emptyResult)
      await expect(awaitModalResult()).resolves.toEqual(emptyResult)
    })

    it("resetModalBridge resolves any pending promise with null", async () => {
      const promise = awaitModalResult()
      resetModalBridge()
      await expect(promise).resolves.toBeNull()
    })

    it("resetModalBridge clears a cached result", async () => {
      const store = useModalStore()
      store.close(emptyResult)
      resetModalBridge()
      const promise = awaitModalResult()
      resolveModal(null)
      await expect(promise).resolves.toBeNull()
    })
  })

  describe("legacy wrappers", () => {
    it("openModal sets currentModalFeatureId and opens modal", async () => {
      const promise = openModal(0, "feat-1")
      const store = useModalStore()
      expect(store.visible).toBe(true)
      expect(store.currentModalFeatureId).toBe("feat-1")
      resolveModal(emptyResult)
      await expect(promise).resolves.toEqual(emptyResult)
    })

    it("openModal sets the city-center label for the cityCenter phase", async () => {
      const promise = openModal(2, "feat-cc")
      const store = useModalStore()
      // t() is stubbed to return the key in tests — the assertion proves the
      // cityCenter branch actually calls t() to label the modal.
      expect(store.label).toBe("phase_cityCenter_label")
      resolveModal(emptyResult)
      await expect(promise).resolves.toEqual(emptyResult)
    })

    it("openModal leaves the label empty for non-cityCenter phases", async () => {
      const promise = openModal(0, "feat-1")
      const store = useModalStore()
      expect(store.label).toBe("")
      resolveModal(emptyResult)
      await expect(promise).resolves.toEqual(emptyResult)
    })

    it("openEditModal opens modal in edit mode", async () => {
      const promise = openEditModal(0, "db-e1", {
        type: "roads",
        label: "Edit Road",
        decisionNumber: "",
        decisionDate: "",
      } as any)
      const store = useModalStore()
      expect(store.isEdit).toBe(true)
      expect(store.editDbId).toBe("db-e1")
      resolveModal(null)
      await expect(promise).resolves.toBeNull()
    })
  })
})
