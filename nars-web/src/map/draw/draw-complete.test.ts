import { describe, it, expect, vi, beforeEach } from "vitest"

const mockClearEdgeVisibilityPoll = vi.hoisted(() => vi.fn())

vi.mock("./draw-control", () => ({ clearEdgeVisibilityPoll: mockClearEdgeVisibilityPoll }))

let geomanMock: any

vi.mock("../core/state", () => ({
  getCtx: () => ({ geoman: geomanMock }),
}))

let mod: typeof import("./draw-complete")

function makeLineDrawer(overrides: Record<string, unknown> = {}) {
  return {
    shapeLngLats: [
      [127.0, 36.0],
      [127.1, 36.1],
    ],
    featureData: {
      markers: new Map(),
      updateGeometry: vi.fn().mockResolvedValue(undefined),
      convertToPolygon: vi.fn().mockResolvedValue(undefined),
      fireUpdateEvent: vi.fn().mockResolvedValue(undefined),
    },
    gm: { markerPointer: { marker: { getLngLat: vi.fn(() => ({ lng: 127.2, lat: 36.2 })) } } },
    getFeatureGeoJson: vi.fn(() => ({ geometry: { type: "LineString", coordinates: [] } })),
    snappingHelper: { setCustomSnappingCoordinates: vi.fn() },
    setSnapping: vi.fn(),
    ...overrides,
  }
}

beforeEach(async () => {
  vi.clearAllMocks()
  mockClearEdgeVisibilityPoll.mockReset()
  mod = await import("./draw-complete")
})

describe("removeLastVertex", () => {
  it("returns early when there is no featureData", async () => {
    geomanMock = { actionInstances: { draw__line: { lineDrawer: {} } } }
    await mod.removeLastVertex()
    expect(mockClearEdgeVisibilityPoll).not.toHaveBeenCalled()
  })

  it("disables draw and clears poll when only one vertex remains", async () => {
    const disableDraw = vi.fn().mockResolvedValue(undefined)
    geomanMock = {
      actionInstances: {
        draw__line: {
          lineDrawer: { featureData: {}, shapeLngLats: [[127.0, 36.0]] },
        },
      },
      disableDraw,
    }
    await mod.removeLastVertex()
    expect(mockClearEdgeVisibilityPoll).toHaveBeenCalled()
    expect(disableDraw).toHaveBeenCalled()
  })

  it("removes the last vertex from a polygon and closes the ring", async () => {
    const lineDrawer = makeLineDrawer()
    const markerData = { instance: { remove: vi.fn() } }
    lineDrawer.featureData.markers.set("vertex-2", markerData)

    geomanMock = {
      actionInstances: {
        draw__polygon: { lineDrawer },
        draw__line: undefined,
      },
      disableDraw: vi.fn(),
    }

    await mod.removeLastVertex()

    expect(markerData.instance.remove).toHaveBeenCalled()
    expect(lineDrawer.featureData.updateGeometry).toHaveBeenCalledWith({
      type: "Polygon",
      coordinates: [
        [
          [127.0, 36.0],
          [127.2, 36.2],
          [127.0, 36.0],
        ],
      ],
    })
    expect(lineDrawer.featureData.convertToPolygon).toHaveBeenCalled()
    expect(lineDrawer.featureData.fireUpdateEvent).toHaveBeenCalled()
    expect(lineDrawer.snappingHelper.setCustomSnappingCoordinates).toHaveBeenCalledWith(undefined, [
      [127.0, 36.0],
    ])
    expect(lineDrawer.setSnapping).toHaveBeenCalled()
  })

  it("updates a line feature using getFeatureGeoJson", async () => {
    const lineDrawer = makeLineDrawer()
    geomanMock = {
      actionInstances: { draw__line: { lineDrawer }, draw__polygon: undefined },
      disableDraw: vi.fn(),
    }

    await mod.removeLastVertex()

    expect(lineDrawer.getFeatureGeoJson).toHaveBeenCalledWith({ withControlMarker: true })
    expect(lineDrawer.featureData.updateGeometry).toHaveBeenCalledWith({
      type: "LineString",
      coordinates: [],
    })
    expect(lineDrawer.setSnapping).toHaveBeenCalled()
  })

  it("handles a polygon without a control marker", async () => {
    const lineDrawer = makeLineDrawer({ gm: { markerPointer: { marker: undefined } } })
    geomanMock = {
      actionInstances: {
        draw__polygon: { lineDrawer },
        draw__line: undefined,
      },
      disableDraw: vi.fn(),
    }

    await mod.removeLastVertex()

    expect(lineDrawer.featureData.updateGeometry).toHaveBeenCalledWith({
      type: "Polygon",
      coordinates: [
        [
          [127.0, 36.0],
          [127.0, 36.0],
        ],
      ],
    })
    expect(lineDrawer.featureData.convertToPolygon).toHaveBeenCalled()
  })
})
