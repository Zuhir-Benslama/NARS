import { describe, it, expect, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { useLayerStore } from "./layerStore"
import type { HouseEntranceFeatureData, LayerEntry } from "../types/features"

function makeEntry(overrides: Partial<LayerEntry> = {}): LayerEntry {
  return {
    id: "id-1",
    dbId: "db-1",
    type: "polygon",
    data: { type: "areas", label: "Test", decisionNumber: "", decisionDate: "", areaTypeKey: "central_urban" },
    ...overrides,
  }
}

describe("layerStore", () => {
  let store: ReturnType<typeof useLayerStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useLayerStore()
  })

  it("has initial empty state", () => {
    expect(store.areas).toEqual([])
    expect(store.cityCenter).toEqual([])
    expect(store.districts).toEqual([])
    expect(store.roads).toEqual([])
    expect(store.houseEntrances).toEqual([])
    expect(store.publicBuildings).toEqual([])
    expect(store.publicSpaces).toEqual([])
    expect(store.namingPanels).toEqual([])
  })

  it("addFeature pushes entry to the named layer", () => {
    const entry = makeEntry()
    store.addFeature("areas", entry)
    expect(store.areas).toHaveLength(1)
    expect(store.areas[0]).toStrictEqual(entry)
  })

  it("removeFeature removes entry by dbId", () => {
    store.addFeature("areas", makeEntry({ dbId: "abc" }))
    store.addFeature("areas", makeEntry({ dbId: "xyz" }))
    store.removeFeature("areas", "abc")
    expect(store.areas).toHaveLength(1)
    expect(store.areas[0].dbId).toBe("xyz")
  })

  it("removeFeature does nothing if dbId not found", () => {
    store.addFeature("areas", makeEntry())
    store.removeFeature("areas", "nonexistent")
    expect(store.areas).toHaveLength(1)
  })

  it("updateFeature patches data by dbId", () => {
    store.addFeature(
      "areas",
      makeEntry({
        dbId: "abc",
        data: { type: "areas", label: "Old", decisionNumber: "", decisionDate: "", areaTypeKey: "central_urban" },
      }),
    )
    store.updateFeature("areas", "abc", { label: "New" })
    expect(store.areas[0].data.label).toBe("New")
  })

  it("updateFeature does nothing for unknown dbId", () => {
    store.addFeature("areas", makeEntry())
    store.updateFeature("areas", "nope", { label: "ShouldNotChange" })
    expect(store.areas[0].data.label).toBe("Test")
  })

  it("clearLayer empties the named layer", () => {
    store.addFeature("areas", makeEntry())
    store.addFeature("districts", makeEntry())
    store.clearLayer("areas")
    expect(store.areas).toHaveLength(0)
    expect(store.districts).toHaveLength(1)
  })

  it("getFeature finds feature across all layers by dbId", () => {
    store.addFeature("areas", makeEntry({ dbId: "abc" }))
    store.addFeature("houseEntrances", makeEntry({ dbId: "xyz" }))
    expect(store.getFeature("abc")).toBeTruthy()
    expect(store.getFeature("xyz")).toBeTruthy()
    expect(store.getFeature("nonexistent")).toBeNull()
  })

  it("reset restores all layers to empty", () => {
    store.addFeature("areas", makeEntry())
    store.addFeature("roads", makeEntry())
    store.reset()
    expect(store.areas).toHaveLength(0)
    expect(store.roads).toHaveLength(0)
  })

  describe("entrance getters", () => {
    const main = (dbId: string) =>
      makeEntry({
        dbId,
        data: {
          type: "houseEntrances",
          label: `Main ${dbId}`,
          entranceTypeKey: "main_entrance",
        },
      })
    const secondary = (dbId: string) =>
      makeEntry({
        dbId,
        data: {
          type: "houseEntrances",
          label: `Sec ${dbId}`,
          entranceTypeKey: "secondary_entrance",
        },
      })

    it("mainEntrances filters by main_entrance", () => {
      store.houseEntrances = [main("1"), main("2"), secondary("3")] as unknown as LayerEntry<HouseEntranceFeatureData>[]
      expect(store.mainEntrances).toHaveLength(2)
    })

    it("secondaryEntrances filters by secondary_entrance", () => {
      store.houseEntrances = [main("1"), secondary("2")] as unknown as LayerEntry<HouseEntranceFeatureData>[]
      expect(store.secondaryEntrances).toHaveLength(1)
    })
  })

  describe("count getters", () => {
    it("reports correct counts for each layer", () => {
      store.addFeature("areas", makeEntry())
      store.addFeature("areas", makeEntry())
      store.addFeature("roads", makeEntry())
      store.addFeature(
        "houseEntrances",
        makeEntry({
          data: {
            type: "houseEntrances",
            label: "M",
            entranceTypeKey: "main_entrance",
          },
        }),
      )

      expect(store.areaCount).toBe(2)
      expect(store.roadCount).toBe(1)
      expect(store.mainEntranceCount).toBe(1)
      expect(store.secondaryEntranceCount).toBe(0)
      expect(store.cityCenterCount).toBe(0)
      expect(store.districtCount).toBe(0)
      expect(store.publicBuildingCount).toBe(0)
      expect(store.publicSpaceCount).toBe(0)
      expect(store.namingPanelCount).toBe(0)
    })
  })
})
