import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useSelectionStore } from "./selectionStore"

describe("selectionStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("starts with null selection", () => {
    const store = useSelectionStore()
    expect(store.selectedFeatureDbId).toBeNull()
  })

  it("setSelectedFeatureDbId sets the db id", () => {
    const store = useSelectionStore()
    store.setSelectedFeatureDbId("abc-123")
    expect(store.selectedFeatureDbId).toBe("abc-123")
  })

  it("setSelectedFeatureDbId with null clears selection", () => {
    const store = useSelectionStore()
    store.setSelectedFeatureDbId("abc-123")
    store.setSelectedFeatureDbId(null)
    expect(store.selectedFeatureDbId).toBeNull()
  })

  it("setSelectedFeatureDbId overwrites previous selection", () => {
    const store = useSelectionStore()
    store.setSelectedFeatureDbId("first-id")
    store.setSelectedFeatureDbId("second-id")
    expect(store.selectedFeatureDbId).toBe("second-id")
  })
})
