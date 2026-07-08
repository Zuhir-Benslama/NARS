import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { LayerEntry } from "../../types"

const {
  mockProject,
  mockSetData,
  mockDebugLog,
  mockDebugWarn,
} = vi.hoisted(() => ({
  mockProject: vi.fn(),
  mockSetData: vi.fn(),
  mockDebugLog: vi.fn(),
  mockDebugWarn: vi.fn(),
}))

vi.mock("../core/state", () => ({
  ctx: {
    map: { project: mockProject },
    endpointsSource: { setData: mockSetData },
  },
}))

vi.mock("../../utils/debug", () => ({
  debugLog: mockDebugLog,
  debugWarn: mockDebugWarn,
}))

vi.mock("../../phases", () => ({
  PHASES: [
    { key: "areas", color: "#ff0000" },
    { key: "roads", color: "#3498db" },
    { key: "cityCenter", color: "#00ff00" },
  ],
}))

let updateEndpointMarkers: () => void

async function reloadModule() {
  const mod = await import("./road-markers")
  updateEndpointMarkers = mod.updateEndpointMarkers
}

function makeRoad(
  id: string,
  dbId: string,
  coords: { lat: number; lng: number }[],
): LayerEntry {
  return {
    id,
    dbId,
    type: "line",
    data: {
      type: "roads",
      label: "",
      decisionNumber: "",
      decisionDate: "",
      coordinates: coords,
    },
  }
}

beforeEach(async () => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  const ctxMod = await import("../core/state")
  ctxMod.ctx.endpointsSource = { setData: mockSetData }
  mockProject.mockReturnValue({ x: 100, y: 200 })
  await reloadModule()
})

describe("road-markers", () => {
  describe("updateEndpointMarkers", () => {
    it("warns and returns when endpointsSource is missing", async () => {
      const ctxMod = await import("../core/state")
      ctxMod.ctx.endpointsSource = undefined

      updateEndpointMarkers()

      expect(mockDebugWarn).toHaveBeenCalled()
      expect(mockSetData).not.toHaveBeenCalled()
    })

    it("creates start and end markers for each road", async () => {
      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ]))
      store.addFeature("roads", makeRoad("r2", "db-2", [
        { lat: 36.02, lng: 127.02 },
        { lat: 36.03, lng: 127.03 },
      ]))

      updateEndpointMarkers()

      expect(mockSetData).toHaveBeenCalledOnce()
      const data = mockSetData.mock.calls[0][0] as GeoJSON.FeatureCollection
      expect(data.features.length).toBe(4)
    })

    it("only creates end marker when start is on city center", async () => {
      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("cityCenter", {
        id: "cc-1",
        dbId: "db-cc-1",
        type: "circle",
        data: { type: "cityCenter", label: "C", decisionNumber: "", decisionDate: "", lat: 36.0, lng: 127.0 },
      })
      store.addFeature("roads", makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ]))

      updateEndpointMarkers()

      const data = mockSetData.mock.calls[0][0] as GeoJSON.FeatureCollection
      expect(data.features.length).toBe(1)
      expect(data.features[0].properties!.endpointType).toBe("end")
    })

    it("suppresses start marker when start is within 2m of city center", async () => {
      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("cityCenter", {
        id: "cc-1",
        dbId: "db-cc-1",
        type: "circle",
        data: { type: "cityCenter", label: "C", decisionNumber: "", decisionDate: "", lat: 36.00001, lng: 127.00001 },
      })
      store.addFeature("roads", makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ]))

      updateEndpointMarkers()

      const data = mockSetData.mock.calls[0][0] as GeoJSON.FeatureCollection
      expect(data.features.length).toBe(1)
    })

    it("skips roads with fewer than 2 coordinates", async () => {
      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", makeRoad("r1", "db-1", [{ lat: 36.0, lng: 127.0 }]))

      updateEndpointMarkers()

      const data = mockSetData.mock.calls[0][0] as GeoJSON.FeatureCollection
      expect(data.features.length).toBe(0)
    })

    it("sets correct angle on markers", async () => {
      mockProject.mockReturnValue({ x: 100, y: 200 })

      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ]))

      updateEndpointMarkers()

      const data = mockSetData.mock.calls[0][0] as GeoJSON.FeatureCollection
      data.features.forEach((f: GeoJSON.Feature) => {
        expect(typeof f.properties!.angle).toBe("number")
      })
    })
  })
})
