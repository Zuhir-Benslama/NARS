import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import {
  useModalStore,
  awaitModalResult,
  openModal,
  openEditModal,
  resolveModal,
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
