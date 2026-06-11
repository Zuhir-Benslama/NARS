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

  it("selectFeature sets the db id", () => {
    const store = useSelectionStore()
    store.selectFeature("abc-123")
    expect(store.selectedFeatureDbId).toBe("abc-123")
  })

  it("selectFeature with null clears selection", () => {
    const store = useSelectionStore()
    store.selectFeature("abc-123")
    store.selectFeature(null)
    expect(store.selectedFeatureDbId).toBeNull()
  })

  it("selectFeature overwrites previous selection", () => {
    const store = useSelectionStore()
    store.selectFeature("first-id")
    store.selectFeature("second-id")
    expect(store.selectedFeatureDbId).toBe("second-id")
  })
})
