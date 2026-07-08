import { describe, it, expect, vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { LayerEntry } from "../../types"

const mockApiFetch = vi.fn()
const mockFeaturesStoreUpdate = vi.fn()
const mockShowToast = vi.fn()
const mockDebugError = vi.fn()
const mockBuildConnectionGraph = vi.fn()
const mockGeographicDirection = vi.fn()
const mockOrientFromCityCenter = vi.fn()
const mockUpdateEndpointMarkers = vi.fn()

vi.mock("../../api", () => ({
  apiFetch: mockApiFetch,
}))

vi.mock("../core/state", () => ({
  featuresStore: {
    update: mockFeaturesStoreUpdate,
  },
}))

vi.mock("../../lib/toast", () => ({
  showToast: mockShowToast,
}))

vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
}))

vi.mock("./road-graph", () => ({
  buildConnectionGraph: mockBuildConnectionGraph,
  dm: vi.fn((a, b) => Math.abs(a.lat - b.lat) * 111320 + Math.abs(a.lng - b.lng) * 111320 * Math.cos(36 * Math.PI / 180)),
  fromNk: vi.fn((k: string) => {
    const [lat, lng] = k.split(",").map(Number)
    return { lat, lng }
  }),
  toPt: vi.fn(),
  toLn: vi.fn(),
  nk: vi.fn(),
}))

vi.mock("./road-orient", () => ({
  geographicDirection: mockGeographicDirection,
  orientFromCityCenter: mockOrientFromCityCenter,
}))

vi.mock("./road-markers", () => ({
  updateEndpointMarkers: mockUpdateEndpointMarkers,
}))

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

function makeGraphMock() {
  const nodes = new Set<string>()
  return {
    nodes: () => nodes,
    addNode: (k: string) => nodes.add(k),
    degree: vi.fn(() => 1),
    order: 0,
    size: 0,
    edges: vi.fn(() => []),
    edge: vi.fn(),
    hasNode: vi.fn(),
    hasEdge: vi.fn(),
    addEdgeWithKey: vi.fn(),
    addDirectedEdgeWithKey: vi.fn(),
    addUndirectedEdgeWithKey: vi.fn(),
    setEdgeAttribute: vi.fn(),
    getEdgeAttribute: vi.fn(),
    updateEdgeAttribute: vi.fn(),
    removeEdge: vi.fn(),
    removeNode: vi.fn(),
    dropEdge: vi.fn(),
    dropNode: vi.fn(),
    clear: vi.fn(),
    forEach: vi.fn(),
    forEachNode: vi.fn(),
    forEachEdge: vi.fn(),
    map: vi.fn(),
    filter: vi.fn(),
    reduce: vi.fn(),
    some: vi.fn(),
    every: vi.fn(),
    find: vi.fn(),
    forEachInNeighbor: vi.fn(),
    forEachOutNeighbor: vi.fn(),
    forEachInEdge: vi.fn(),
    forEachOutEdge: vi.fn(),
    forEachEdgeOf: vi.fn(),
    forEachNeighborOf: vi.fn(),
    [Symbol.iterator]: vi.fn(),
  }
}

let computeAndApplyRoadDirections: () => Promise<void>

async function reloadModule() {
  const mod = await import("./road-directions")
  computeAndApplyRoadDirections = mod.computeAndApplyRoadDirections
}

beforeEach(async () => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  mockApiFetch.mockResolvedValue({ ok: true })
  await reloadModule()
})

describe("road-directions", () => {
  describe("computeAndApplyRoadDirections", () => {
    it("returns early when no roads exist", async () => {
      await computeAndApplyRoadDirections()
      expect(mockBuildConnectionGraph).not.toHaveBeenCalled()
      expect(mockShowToast).not.toHaveBeenCalled()
    })

    it("processes roads without city centers using geographic fallback", async () => {
      const graph = makeGraphMock()
      const segs = new Map()
      segs.set("s1", {
        coords: [{ lat: 36.0, lng: 127.0 }],
        entry: makeRoad("r1", "db-1", [{ lat: 36.0, lng: 127.0 }]),
        dbId: "db-1",
        reversed: false,
      })
      mockBuildConnectionGraph.mockReturnValue({ graph, segs })

      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ]))

      await computeAndApplyRoadDirections()

      expect(mockBuildConnectionGraph).toHaveBeenCalledOnce()
      expect(mockGeographicDirection).toHaveBeenCalled()
      expect(mockShowToast).toHaveBeenCalled()
    })

    it("uses orientFromCityCenter when city centers exist", async () => {
      const graph = makeGraphMock()
      const segs = new Map()
      mockBuildConnectionGraph.mockReturnValue({ graph, segs })

      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ]))
      store.addFeature("cityCenter", {
        id: "cc-1",
        dbId: "db-cc-1",
        type: "circle",
        data: { type: "cityCenter", label: "C", decisionNumber: "", decisionDate: "", lat: 36.0, lng: 127.0, radius: 50 },
      })

      await computeAndApplyRoadDirections()

      expect(mockOrientFromCityCenter).toHaveBeenCalled()
    })

    it("reverses road when rev votes exceed fwd votes", async () => {
      const graph = makeGraphMock()
      graph.nodes().add("36.00000,127.00000")
      graph.nodes().add("36.01000,127.00000")
      graph.degree.mockReturnValue(2)

      const road = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])

      const segs = new Map()
      segs.set("s1", {
        coords: [
          { lat: 36.0, lng: 127.0 },
          { lat: 36.01, lng: 127.01 },
        ],
        entry: road,
        dbId: "db-1",
        reversed: true,
      })

      mockBuildConnectionGraph.mockReturnValue({ graph, segs })
      mockApiFetch.mockResolvedValue({ ok: true })

      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", road)

      await computeAndApplyRoadDirections()

      expect(mockFeaturesStoreUpdate).toHaveBeenCalledWith(
        "r1",
        expect.objectContaining({ geometry: expect.objectContaining({ type: "LineString" }) }),
      )
      expect(mockApiFetch).toHaveBeenCalled()
      expect(mockUpdateEndpointMarkers).toHaveBeenCalledOnce()
    })

    it("does not reverse road when fwd votes exceed rev votes", async () => {
      const graph = makeGraphMock()
      const road = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])
      const segs = new Map()
      segs.set("s1", {
        coords: [
          { lat: 36.0, lng: 127.0 },
          { lat: 36.01, lng: 127.01 },
        ],
        entry: road,
        dbId: "db-1",
        reversed: false,
      })
      mockBuildConnectionGraph.mockReturnValue({ graph, segs })

      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", road)

      await computeAndApplyRoadDirections()

      expect(mockFeaturesStoreUpdate).not.toHaveBeenCalled()
      expect(mockApiFetch).not.toHaveBeenCalled()
    })

    it("handles API save failure gracefully", async () => {
      const graph = makeGraphMock()
      graph.nodes().add("36.00000,127.00000")
      graph.nodes().add("36.01000,127.00000")
      graph.degree.mockReturnValue(2)

      const road = makeRoad("r1", "db-1", [
        { lat: 36.0, lng: 127.0 },
        { lat: 36.01, lng: 127.01 },
      ])
      const segs = new Map()
      segs.set("s1", {
        coords: [
          { lat: 36.0, lng: 127.0 },
          { lat: 36.01, lng: 127.01 },
        ],
        entry: road,
        dbId: "db-1",
        reversed: true,
      })
      mockBuildConnectionGraph.mockReturnValue({ graph, segs })
      mockApiFetch.mockRejectedValue(new Error("Network error"))

      const { useLayerStore } = await import("../../stores/layerStore")
      const store = useLayerStore()
      store.addFeature("roads", road)

      await computeAndApplyRoadDirections()

      expect(mockDebugError).toHaveBeenCalled()
      expect(mockUpdateEndpointMarkers).toHaveBeenCalledOnce()
    })
  })
})
