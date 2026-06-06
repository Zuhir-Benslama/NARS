import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useFieldStore } from "./fieldStore"
import type { InspectionType } from "../types/inspection"

describe("useFieldStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("initializes with default state", () => {
    const store = useFieldStore()
    expect(store.selectedFeature).toBeNull()
    expect(store.hasSelection).toBe(false)
    expect(store.featureType).toBeNull()
  })

  it("selectFeature sets the selected feature", () => {
    const store = useFieldStore()
    store.selectFeature({ id: "1", label: "Test Feature", type: "building" as InspectionType })
    expect(store.selectedFeature).toEqual({ id: "1", label: "Test Feature", type: "building" })
    expect(store.hasSelection).toBe(true)
    expect(store.featureType).toBe("building")
  })

  it("selectFeature with null clears selection", () => {
    const store = useFieldStore()
    store.selectFeature({ id: "1", label: "Test", type: "road" as InspectionType })
    store.selectFeature(null)
    expect(store.selectedFeature).toBeNull()
    expect(store.hasSelection).toBe(false)
    expect(store.featureType).toBeNull()
  })

  it("clearSelection resets to null", () => {
    const store = useFieldStore()
    store.selectFeature({ id: "2", label: "Other", type: "public_space" as InspectionType })
    store.clearSelection()
    expect(store.selectedFeature).toBeNull()
  })

  it("featureType getter returns null when no selection", () => {
    const store = useFieldStore()
    expect(store.featureType).toBeNull()
  })
})
