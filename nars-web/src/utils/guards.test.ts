import { describe, it, expect } from "vitest"

describe("type guards", () => {
  async function freshGuards() {
    return await import("./guards")
  }

  it("isPoint returns true for Point geometry", async () => {
    const { isPoint } = await freshGuards()
    expect(isPoint({ type: "Point", coordinates: [1, 2] })).toBe(true)
  })

  it("isPoint returns false for non-Point geometry", async () => {
    const { isPoint } = await freshGuards()
    expect(
      isPoint({
        type: "LineString",
        coordinates: [
          [1, 2],
          [3, 4],
        ],
      }),
    ).toBe(false)
  })

  it("isLineString returns true for LineString", async () => {
    const { isLineString } = await freshGuards()
    expect(
      isLineString({
        type: "LineString",
        coordinates: [
          [1, 2],
          [3, 4],
        ],
      }),
    ).toBe(true)
  })

  it("isLineString returns false for non-LineString", async () => {
    const { isLineString } = await freshGuards()
    expect(isLineString({ type: "Point", coordinates: [1, 2] })).toBe(false)
  })

  it("isPolygon returns true for Polygon", async () => {
    const { isPolygon } = await freshGuards()
    expect(
      isPolygon({
        type: "Polygon",
        coordinates: [
          [
            [1, 2],
            [3, 4],
            [5, 6],
            [1, 2],
          ],
        ],
      }),
    ).toBe(true)
  })

  it("isPolygon returns false for non-Polygon", async () => {
    const { isPolygon } = await freshGuards()
    expect(isPolygon({ type: "Point", coordinates: [1, 2] })).toBe(false)
  })

  it("isMultiPolygon returns true for MultiPolygon", async () => {
    const { isMultiPolygon } = await freshGuards()
    expect(
      isMultiPolygon({
        type: "MultiPolygon",
        coordinates: [
          [
            [
              [1, 2],
              [3, 4],
              [1, 2],
            ],
          ],
        ],
      }),
    ).toBe(true)
  })

  it("isMultiLineString returns true for MultiLineString", async () => {
    const { isMultiLineString } = await freshGuards()
    expect(
      isMultiLineString({
        type: "MultiLineString",
        coordinates: [
          [
            [1, 2],
            [3, 4],
          ],
        ],
      }),
    ).toBe(true)
  })

  it("isMultiPoint returns true for MultiPoint", async () => {
    const { isMultiPoint } = await freshGuards()
    expect(
      isMultiPoint({
        type: "MultiPoint",
        coordinates: [
          [1, 2],
          [3, 4],
        ],
      }),
    ).toBe(true)
  })
})
