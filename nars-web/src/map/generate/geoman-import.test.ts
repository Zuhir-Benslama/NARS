import { describe, it, expect, vi, beforeEach } from "vitest"

const { mockEnsureGeoman, mockDebugError } = vi.hoisted(() => ({
  mockEnsureGeoman: vi.fn(),
  mockDebugError: vi.fn(),
}))

let geomanMock: any

vi.mock("../map-init", () => ({ ensureGeoman: mockEnsureGeoman }))
vi.mock("../core/state", () => ({
  getCtx: () => ({ geoman: geomanMock }),
  resetMapState: vi.fn(),
}))
vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))

import { importFeaturesIntoGeoman } from "./geoman-import"

function lineEntry(
  dbId: string,
  coords: [number, number][] = [
    [3, 36.7],
    [3.01, 36.71],
  ],
): any {
  return {
    id: `feat_${dbId}`,
    dbId,
    data: {
      type: "roads",
      label: "",
      coordinates: coords.map(([lng, lat]) => ({ lat, lng })),
      decisionNumber: "",
      decisionDate: "",
      roadTypeKey: "street",
    },
    type: "line" as const,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  geomanMock = undefined
  mockEnsureGeoman.mockResolvedValue(undefined)
})

describe("importFeaturesIntoGeoman", () => {
  it("returns without touching geoman when there are no entries", async () => {
    await importFeaturesIntoGeoman([])
    expect(mockEnsureGeoman).not.toHaveBeenCalled()
    expect(geomanMock).toBeUndefined()
  })

  it("imports each line entry into geoman with overwrite", async () => {
    geomanMock = { features: { importGeoJson: vi.fn().mockResolvedValue({}) } }

    const result = await importFeaturesIntoGeoman([lineEntry("r1"), lineEntry("r2")])

    expect(result).toBe(2)
    expect(mockEnsureGeoman).toHaveBeenCalledTimes(1)
    expect(geomanMock.features.importGeoJson).toHaveBeenCalledTimes(2)
    const [call] = geomanMock.features.importGeoJson.mock.calls

    expect(call[0].geometry).toMatchObject({
      type: "LineString",
      coordinates: [
        [3, 36.7],
        [3.01, 36.71],
      ],
    })
    expect(call[0].properties).toMatchObject({ shape: "line", dbId: "r1" })
    expect(call[1]).toEqual({ overwrite: true })
  })

  it("swallows geoman init failures so roads still render from stores", async () => {
    mockEnsureGeoman.mockRejectedValue(new Error("boilerplate missing"))

    await expect(importFeaturesIntoGeoman([lineEntry("r1")])).resolves.toBe(0)
    expect(mockDebugError).toHaveBeenCalled()
  })

  it("swallows per-feature import failures and keeps going", async () => {
    geomanMock = {
      features: {
        importGeoJson: vi.fn().mockRejectedValueOnce(new Error("boom")).mockResolvedValueOnce({}),
      },
    }

    const result = await importFeaturesIntoGeoman([lineEntry("r1"), lineEntry("r2")])

    expect(result).toBe(1)
    expect(geomanMock.features.importGeoJson).toHaveBeenCalledTimes(2)
    expect(mockDebugError).toHaveBeenCalledTimes(1)
  })

  it("stops when geoman is not available after init", async () => {
    geomanMock = null

    const result = await importFeaturesIntoGeoman([lineEntry("r1")])

    expect(result).toBe(0)
    expect(mockEnsureGeoman).toHaveBeenCalledTimes(1)
    expect(mockDebugError).not.toHaveBeenCalled()
  })
})
