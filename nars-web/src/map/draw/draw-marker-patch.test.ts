import { describe, it, expect, vi, beforeEach } from "vitest"
import type { GeomanMarkerPointer } from "../core/geoman-types"

const {
  mockGetFrozenSnapPos,
  mockGetActiveSnapPhases,
  mockFindNearestSnap,
  mockMergeExternalSnap,
  mockRegisterGeomanMarker,
} = vi.hoisted(() => ({
  mockGetFrozenSnapPos: vi.fn(),
  mockGetActiveSnapPhases: vi.fn(),
  mockFindNearestSnap: vi.fn(),
  mockMergeExternalSnap: vi.fn(),
  mockRegisterGeomanMarker: vi.fn(),
}))

let projectMock: any

vi.mock("../core/state", () => ({
  getCtx: () => ({ map: { project: projectMock }, geoman: null }),
}))
vi.mock("../snapping/snapping", () => ({
  getFrozenSnapPos: mockGetFrozenSnapPos,
  getActiveSnapPhases: mockGetActiveSnapPhases,
}))
vi.mock("../snapping/snap-search", () => ({
  findNearestSnap: mockFindNearestSnap,
  mergeExternalSnapWithDrawFirstVertex: mockMergeExternalSnap,
}))
vi.mock("../draw/draw-complete", () => ({ registerGeomanMarker: mockRegisterGeomanMarker }))
vi.mock("../../utils/debug", () => ({ debugLog: vi.fn(), debugWarn: vi.fn() }))

let mod: typeof import("./draw-marker-patch")

beforeEach(async () => {
  vi.resetAllMocks()
  projectMock = vi.fn(([lng, lat]: [number, number]) => ({ x: lng * 10, y: lat * 10 }))
  mockGetActiveSnapPhases.mockReturnValue([])
  mod = await import("./draw-marker-patch")
})

function setup() {
  const mp: GeomanMarkerPointer = {
    marker: { setLngLat: vi.fn(), getLngLat: vi.fn() },
  }
  const orig = vi.fn()
  const setLngLat = mod.makeSnapSetLngLat(mp, orig)
  return { mp, orig, setLngLat }
}

describe("makeSnapSetLngLat", () => {
  it("applies the NARS snap when one is found", () => {
    mockGetActiveSnapPhases.mockReturnValue(["district"])
    mockMergeExternalSnap.mockReturnValue({ lng: 9, lat: 8 })
    const { orig, setLngLat } = setup()

    setLngLat([127.5, 36.5])

    expect(projectMock).toHaveBeenCalledWith([127.5, 36.5])
    expect(mockFindNearestSnap).toHaveBeenCalled()
    expect(orig).toHaveBeenCalledWith([9, 8])
    expect(mod.getNarsLastSnap()).toEqual({ lng: 9, lat: 8 })
  })

  it("passes through the original coordinates when no snap matches", () => {
    const { orig, setLngLat } = setup()
    setLngLat([127.5, 36.5])
    expect(orig).toHaveBeenCalledWith([127.5, 36.5])
    expect(mod.getNarsLastSnap()).toBeNull()
  })

  it("uses the frozen snap position when set", () => {
    mockGetFrozenSnapPos.mockReturnValue({ lng: 3, lat: 4 })
    const { orig, setLngLat } = setup()
    setLngLat([10, 20])
    expect(orig).toHaveBeenCalledWith([3, 4])
    expect(mod.getNarsLastSnap()).toEqual({ lng: 3, lat: 4 })
    expect(mockFindNearestSnap).not.toHaveBeenCalled()
  })

  it("accepts a LngLat-like object via toArray", () => {
    const { orig, setLngLat } = setup()
    setLngLat({ lng: 0, lat: 0, toArray: () => [5, 6] })
    expect(projectMock).toHaveBeenCalledWith([5, 6])
    expect(orig).toHaveBeenCalledWith({ lng: 0, lat: 0, toArray: expect.any(Function) })
    expect(mod.getNarsLastSnap()).toBeNull()
  })
})
