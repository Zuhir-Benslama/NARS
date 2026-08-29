import { describe, it, expect, vi, beforeEach } from "vitest"
import type { LayerEntry } from "../../types"

const mockComputeCircleRingForEdit = vi.hoisted(() =>
  vi.fn((): [number, number][] => [
    [1, 2],
    [3, 4],
  ]),
)

vi.mock("../rendering/geometry", () => ({
  computeCircleRingForEdit: mockComputeCircleRingForEdit,
}))

let mod: typeof import("./edit-import")

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  mod = await import("./edit-import")
})

function entry(overrides: Record<string, unknown> = {}): LayerEntry {
  return {
    id: "feat_1",
    dbId: "db-1",
    type: "polygon",
    data: { type: "areas", label: "A" },
    ...overrides,
  } as unknown as LayerEntry
}

describe("buildGeomanImportFeature", () => {
  it("builds a closed LineString ring for a circle", () => {
    const result = mod.buildGeomanImportFeature(
      entry({
        type: "circle",
        data: { type: "cityCenter", label: "C", lat: 36.5, lng: 127.5, radius: 20 },
      }),
    )
    expect(mockComputeCircleRingForEdit).toHaveBeenCalledWith(36.5, 127.5, 20)
    expect(result).toEqual({
      type: "Feature",
      geometry: {
        type: "LineString",
        coordinates: [
          [1, 2],
          [3, 4],
          [1, 2],
        ],
      },
      properties: { shape: "line", dbId: "db-1" },
    })
  })

  it("builds a Point for a feature with lat/lng", () => {
    const result = mod.buildGeomanImportFeature(
      entry({
        type: "marker",
        data: {
          type: "houseEntrances",
          label: "H",
          lat: 36.5,
          lng: 127.5,
          entranceTypeKey: "main_entrance",
        },
      }),
    )
    expect(result).toEqual({
      type: "Feature",
      geometry: { type: "Point", coordinates: [127.5, 36.5] },
      properties: { shape: "marker", dbId: "db-1" },
    })
  })

  it("builds a LineString for a line feature", () => {
    const result = mod.buildGeomanImportFeature(
      entry({
        type: "line",
        data: {
          type: "roads",
          label: "R",
          coordinates: [
            { lat: 36.9, lng: 127.9 },
            { lat: 37.0, lng: 128.0 },
          ],
        },
      }),
    )
    expect(result).toEqual({
      type: "Feature",
      geometry: {
        type: "LineString",
        coordinates: [
          [127.9, 36.9],
          [128.0, 37.0],
        ],
      },
      properties: { shape: "line", dbId: "db-1" },
    })
  })

  it("closes an open polygon ring", () => {
    const result = mod.buildGeomanImportFeature(
      entry({
        type: "polygon",
        data: {
          type: "areas",
          label: "A",
          coordinates: [
            { lat: 1, lng: 1 },
            { lat: 2, lng: 2 },
          ],
        },
      }),
    )
    expect(result).toEqual({
      type: "Feature",
      geometry: {
        type: "Polygon",
        coordinates: [
          [
            [1, 1],
            [2, 2],
            [1, 1],
          ],
        ],
      },
      properties: { shape: "polygon", dbId: "db-1" },
    })
  })

  it("keeps an already-closed polygon ring unchanged", () => {
    const result = mod.buildGeomanImportFeature(
      entry({
        type: "polygon",
        data: {
          type: "areas",
          label: "A",
          coordinates: [
            { lat: 1, lng: 1 },
            { lat: 2, lng: 2 },
            { lat: 1, lng: 1 },
          ],
        },
      }),
    )
    expect(result).toEqual({
      type: "Feature",
      geometry: {
        type: "Polygon",
        coordinates: [
          [
            [1, 1],
            [2, 2],
            [1, 1],
          ],
        ],
      },
      properties: { shape: "polygon", dbId: "db-1" },
    })
  })

  it("returns null when no geometry information is present", () => {
    const result = mod.buildGeomanImportFeature(
      entry({ type: "polygon", data: { type: "areas", label: "A" } }),
    )
    expect(result).toBeNull()
  })
})
