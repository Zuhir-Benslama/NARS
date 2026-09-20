import { describe, it, expect } from "vitest"
import {
  lonToTileX,
  latToTileY,
  latFromTileY,
  tileGrid,
  chooseTileZoom,
  gridBounds,
} from "./satellite-tiler"

const WORLD = { minLon: -180, minLat: -90, maxLon: 180, maxLat: 90 }
const ALGIERS = { minLon: 2.9, minLat: 36.7, maxLon: 3.1, maxLat: 36.8 }

describe("slippy-index math", () => {
  it("maps the whole world to the single z=0 tile", () => {
    expect(lonToTileX(-180, 0)).toBe(0)
    expect(latToTileY(85.05112878, 0)).toBe(0)
    const grid = tileGrid(WORLD, 0)
    expect(grid).toEqual({ zoom: 0, x0: 0, y0: 0, width: 1, height: 1 })
  })

  it("maps the Mercator latitude limit to tile row 0", () => {
    expect(latToTileY(85.05112878, 10)).toBe(0)
    expect(latToTileY(90, 10)).toBe(0)
  })

  it("is monotonic in longitude and latitude", () => {
    const lon = [-10, -1, 0, 1, 10]
    const x = lon.map((v) => lonToTileX(v, 12))
    for (let i = 1; i < x.length; i += 1) expect(x[i]).toBeGreaterThanOrEqual(x[i - 1])

    const lat = [10, 1, 0, -1, -10]
    const y = lat.map((v) => latToTileY(v, 12))
    for (let i = 1; i < y.length; i += 1) expect(y[i]).toBeGreaterThanOrEqual(y[i - 1])
  })

  it("produces decreasing latitudes for increasing tile rows", () => {
    const z = 6
    const rows = [0, 1, 2, 3, 63, 64]
    for (let i = 1; i < rows.length; i += 1) {
      expect(latFromTileY(rows[i], z)).toBeLessThan(latFromTileY(rows[i - 1], z))
    }
  })
})

describe("chooseTileZoom", () => {
  it("keeps the grid within the 24x24-tile cap", () => {
    for (const bounds of [
      WORLD,
      ALGIERS,
      { minLon: 2.95, minLat: 36.72, maxLon: 3.05, maxLat: 36.78 },
    ]) {
      const zoom = chooseTileZoom(bounds, 18)
      const { width, height } = tileGrid(bounds, zoom)
      expect(width).toBeLessThanOrEqual(24)
      expect(height).toBeLessThanOrEqual(24)
      expect(width * height).toBeLessThanOrEqual(576)
    }
  })

  it("prefers the highest allowed zoom when the area is small", () => {
    const zoom = chooseTileZoom(ALGIERS, 18)
    const { width, height } = tileGrid(ALGIERS, zoom)
    expect(width).toBeLessThanOrEqual(24)
    expect(height).toBeLessThanOrEqual(24)
    // one zoom higher would blow the cap on at least one axis
    const next = tileGrid(ALGIERS, zoom + 1)
    expect(next.width > 24 || next.height > 24).toBe(true)
  })

  it("stays at the max satellite zoom for commune-scale draws so roads resolve", () => {
    // The user-facing regression: a commune urban-area box (~2.4 x 2.2 km,
    // the Bir Bouhouche central_urban extent) must pick z18 (~0.48 m/px) —
    // the resolution the SpaceNet roads model was trained at, where it
    // resolves street centerlines that blur together at z17 (the earlier
    // symptom was "generated few/no roads").
    const communeScale = {
      minLon: 7.416891380448902,
      minLat: 36.00475978679174,
      maxLon: 7.4413371570909135,
      maxLat: 36.0248693565875,
    }
    const zoom = chooseTileZoom(communeScale, 18)
    expect(zoom).toBe(18)
    const { width, height } = tileGrid(communeScale, zoom)
    expect(width).toBeLessThanOrEqual(24)
    expect(height).toBeLessThanOrEqual(24)
  })

  it("returns 0 for areas that exceed the cap even at z=1", () => {
    const zoom = chooseTileZoom(WORLD, 0)
    expect(zoom).toBe(0)
  })
})

describe("gridBounds", () => {
  it("covers the requested bounds", () => {
    const grid = tileGrid(ALGIERS, chooseTileZoom(ALGIERS, 17))
    const gb = gridBounds(grid)
    expect(gb.minLon).toBeLessThanOrEqual(ALGIERS.minLon)
    expect(gb.maxLon).toBeGreaterThanOrEqual(ALGIERS.maxLon)
    expect(gb.minLat).toBeLessThanOrEqual(ALGIERS.minLat)
    expect(gb.maxLat).toBeGreaterThanOrEqual(ALGIERS.maxLat)
  })

  it("yields the world extent at z=0", () => {
    const gb = gridBounds({ zoom: 0, x0: 0, y0: 0, width: 1, height: 1 })
    expect(gb.minLon).toBeCloseTo(-180, 6)
    expect(gb.maxLon).toBeCloseTo(180, 6)
    expect(gb.maxLat).toBeCloseTo(85.05112878, 6)
    expect(gb.minLat).toBeCloseTo(-85.05112878, 6)
  })
})
