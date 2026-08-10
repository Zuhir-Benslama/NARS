import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import { setReferenceRoad, clearReferenceRoad, setReferenceEntrance } from "./house-entrances"
import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import { useFeaturesStore } from "../stores/featuresStore"

vi.mock("../lib/toast", () => ({ showToast: vi.fn() }))

vi.mock("./core/state", () => ({ getCtx: () => ({}) }))

const REFERENCE_COLOR = "#f39c12"
const DEFAULT_ROAD_COLOR = "#3498db"
const DEFAULT_ENTRANCE_COLOR = "#27ae60"

const ROAD_GEOMETRY = JSON.stringify({
  type: "LineString",
  coordinates: [
    [0, 0],
    [1, 1],
  ],
})
const ENTRANCE_GEOMETRY = JSON.stringify({ type: "Point", coordinates: [0, 0] })

function seedStores() {
  const appStore = useAppStore()
  const layerStore = useLayerStore()
  const featuresStore = useFeaturesStore()

  layerStore.$patch({
    roads: [
      {
        id: "f-road-1",
        dbId: "road-1",
        type: "line",
        data: {
          type: "roads",
          label: "R1",
          decisionNumber: "1",
          decisionDate: "2020-01-01",
          roadTypeKey: "main_road",
          geometry: ROAD_GEOMETRY,
        },
      },
    ],
    houseEntrances: [
      {
        id: "f-ent-1",
        dbId: "ent-1",
        type: "marker",
        data: {
          type: "houseEntrances",
          label: "E1",
          entranceTypeKey: "main_entrance",
          geometry: ENTRANCE_GEOMETRY,
        },
      },
    ],
  })

  featuresStore.features = [
    {
      id: "f-road-1",
      geometry: JSON.parse(ROAD_GEOMETRY),
      properties: {
        dbId: "road-1",
        phaseKey: "roads",
        label: "R1",
        lineColor: DEFAULT_ROAD_COLOR,
      },
    },
    {
      id: "f-ent-1",
      geometry: JSON.parse(ENTRANCE_GEOMETRY),
      properties: {
        dbId: "ent-1",
        phaseKey: "houseEntrances",
        label: "E1",
        circleColor: DEFAULT_ENTRANCE_COLOR,
      },
    },
  ]

  return { appStore, layerStore, featuresStore }
}

describe("house-entrances", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  describe("setReferenceRoad", () => {
    it("sets the reference road and highlights it", () => {
      const { appStore, featuresStore } = seedStores()
      setReferenceRoad("road-1")
      expect(appStore.referenceRoadDbId).toBe("road-1")
      const road = featuresStore.features.find((f) => f.id === "f-road-1")!
      expect(road.properties.lineColor).toBe(REFERENCE_COLOR)
    })

    it("unhighlights the previous reference road when switching", () => {
      const { featuresStore } = seedStores()
      useLayerStore().$patch({
        roads: [
          ...useLayerStore().roads,
          {
            id: "f-road-2",
            dbId: "road-2",
            type: "line",
            data: {
              type: "roads",
              label: "R2",
              decisionNumber: "2",
              decisionDate: "2020-01-01",
              roadTypeKey: "main_road",
              geometry: ROAD_GEOMETRY,
            },
          },
        ],
      })
      featuresStore.features.push({
        id: "f-road-2",
        geometry: JSON.parse(ROAD_GEOMETRY),
        properties: {
          dbId: "road-2",
          phaseKey: "roads",
          label: "R2",
          lineColor: DEFAULT_ROAD_COLOR,
        },
      })

      setReferenceRoad("road-1")
      setReferenceRoad("road-2")

      const road1 = featuresStore.features.find((f) => f.id === "f-road-1")!
      const road2 = featuresStore.features.find((f) => f.id === "f-road-2")!
      expect(road1.properties.lineColor).toBe(DEFAULT_ROAD_COLOR)
      expect(road2.properties.lineColor).toBe(REFERENCE_COLOR)
    })

    it("is a no-op when the road is not in the layer store", () => {
      const { appStore } = seedStores()
      expect(() => setReferenceRoad("missing")).not.toThrow()
      expect(appStore.referenceRoadDbId).toBe("missing")
    })
  })

  describe("clearReferenceRoad", () => {
    it("clears the reference and restores the default color", () => {
      const { appStore, featuresStore } = seedStores()
      setReferenceRoad("road-1")
      clearReferenceRoad()
      expect(appStore.referenceRoadDbId).toBeNull()
      const road = featuresStore.features.find((f) => f.id === "f-road-1")!
      expect(road.properties.lineColor).toBe(DEFAULT_ROAD_COLOR)
    })

    it("is a no-op when no road is selected", () => {
      seedStores()
      expect(() => clearReferenceRoad()).not.toThrow()
    })
  })

  describe("setReferenceEntrance", () => {
    it("sets the reference entrance and highlights it", () => {
      const { appStore, featuresStore } = seedStores()
      setReferenceEntrance("ent-1")
      expect(appStore.referenceEntranceDbId).toBe("ent-1")
      const ent = featuresStore.features.find((f) => f.id === "f-ent-1")!
      expect(ent.properties.circleColor).toBe(REFERENCE_COLOR)
    })

    it("unhighlights the previous reference entrance when switching", () => {
      const { featuresStore } = seedStores()
      useLayerStore().$patch({
        houseEntrances: [
          ...useLayerStore().houseEntrances,
          {
            id: "f-ent-2",
            dbId: "ent-2",
            type: "marker",
            data: {
              type: "houseEntrances",
              label: "E2",
              entranceTypeKey: "secondary_entrance",
              geometry: ENTRANCE_GEOMETRY,
            },
          },
        ],
      })
      featuresStore.features.push({
        id: "f-ent-2",
        geometry: JSON.parse(ENTRANCE_GEOMETRY),
        properties: {
          dbId: "ent-2",
          phaseKey: "houseEntrances",
          label: "E2",
          circleColor: DEFAULT_ENTRANCE_COLOR,
        },
      })

      setReferenceEntrance("ent-1")
      setReferenceEntrance("ent-2")

      const ent1 = featuresStore.features.find((f) => f.id === "f-ent-1")!
      const ent2 = featuresStore.features.find((f) => f.id === "f-ent-2")!
      expect(ent1.properties.circleColor).toBe(DEFAULT_ENTRANCE_COLOR)
      expect(ent2.properties.circleColor).toBe(REFERENCE_COLOR)
    })

    it("still updates the color when geometry is invalid JSON", () => {
      const { featuresStore } = seedStores()
      useLayerStore().$patch({
        houseEntrances: [
          {
            id: "f-ent-3",
            dbId: "ent-3",
            type: "marker",
            data: {
              type: "houseEntrances",
              label: "E3",
              entranceTypeKey: "main_entrance",
              geometry: "{not valid json",
            },
          },
        ],
      })
      featuresStore.features.push({
        id: "f-ent-3",
        geometry: JSON.parse(ENTRANCE_GEOMETRY),
        properties: {
          dbId: "ent-3",
          phaseKey: "houseEntrances",
          label: "E3",
          circleColor: DEFAULT_ENTRANCE_COLOR,
        },
      })

      setReferenceEntrance("ent-3")
      const ent = featuresStore.features.find((f) => f.id === "f-ent-3")!
      expect(ent.properties.circleColor).toBe(REFERENCE_COLOR)
    })
  })
})
