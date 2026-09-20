// ─── SATELLITE TILE COMPOSITOR ────────────────────────────────────────────────
// Renders a geographic bounding box onto slippy-map satellite tiles at the
// highest zoom whose tile count fits in the cap, and returns a single JPEG blob
// together with the exact (grid-aligned) bounds of the composed image. The
// resulting ortho image georeferences linearly to those bounds, which is what
// the segmentation endpoint needs to map model outputs back to coordinates.
// JPEG is used instead of PNG because a full 24x24 tile grid (6144x6144 px) of
// photographic satellite imagery can exceed the 50MB upload cap as a PNG (PNG
// compresses photos poorly), whereas the same image as a quality-85 JPEG is
// typically well under 20MB.
//
// Pure helper functions (slippy-index math, zoom selection, grid bounds) are
// exported for unit testing; only renderSatelliteTile touches the DOM.

import { MAP_CONFIG } from "../../config"
import type { TileBounds } from "../../api/drafts"

export interface RenderedSatelliteTile {
  blob: Blob
  bounds: TileBounds
  width: number
  height: number
}

const TILE_SIZE = 256
// segma decomposes the uploaded tile into 1024px inference windows (default
// NARS_SEGMA_TILE_SIZE), one model pass each. The cap is the per-axis grid
// budget; 24 tiles per axis (6144x6144 px) keeps a typical commune-scale draw
// at the satellite source's max zoom (z18, ~0.48 m/px) — the resolution the
// SpaceNet roads model was trained on, where it resolves street centerlines
// that blur together at z17. A 24x24 grid stays inside the 180s generation
// timeout (worst case 6x6 = 36 windows, each ~4s) and the segma decoded-pixel
// allowance. The earlier 16x16 cap was the thing forcing z17 and the "few
// roads" symptom; before that a 4x4 cap forced a coarser zoom where the road
// model detected nothing.
const MAX_GRID_DIM = 24
const LAT_LIMIT = 85.05112878

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}

/** Slippy-map tile column for a longitude at zoom (Web Mercator). */
export function lonToTileX(lon: number, zoom: number): number {
  const n = 2 ** zoom
  const x = ((lon + 180) / 360) * n
  if (x <= 0) return 0
  if (x >= n) return n - 1
  return Math.floor(x)
}

/** Slippy-map tile row for a latitude at zoom (Web Mercator). */
export function latToTileY(lat: number, zoom: number): number {
  const n = 2 ** zoom
  const latRad = (clamp(lat, -LAT_LIMIT, LAT_LIMIT) * Math.PI) / 180
  const mercY = Math.log(Math.tan(latRad) + 1 / Math.cos(latRad))
  const y = ((1 - mercY / Math.PI) / 2) * n
  if (y <= 0) return 0
  if (y >= n) return n - 1
  return Math.floor(y)
}

/** Inverse of latToTileY — latitude of a tile row's north edge. */
export function latFromTileY(y: number, zoom: number): number {
  const n = 2 ** zoom
  const latRad = Math.atan(Math.sinh(Math.PI * (1 - (2 * y) / n)))
  return (latRad * 180) / Math.PI
}

export interface TileGrid {
  zoom: number
  x0: number
  y0: number
  width: number // tiles in a row
  height: number // tiles in a column
}

/** Tile-grid covering a bounds at the given zoom. */
export function tileGrid(bounds: TileBounds, zoom: number): TileGrid {
  const x0 = lonToTileX(bounds.minLon, zoom)
  const x1 = lonToTileX(bounds.maxLon, zoom)
  const y0 = latToTileY(bounds.maxLat, zoom)
  const y1 = latToTileY(bounds.minLat, zoom)
  return {
    zoom,
    x0,
    y0,
    width: Math.max(1, x1 - x0 + 1),
    height: Math.max(1, y1 - y0 + 1),
  }
}

/** Highest zoom at or below max whose grid stays within MAX_GRID_DIM per axis. */
export function chooseTileZoom(bounds: TileBounds, maxZoom: number): number {
  for (let zoom = maxZoom; zoom > 0; zoom -= 1) {
    const { width, height } = tileGrid(bounds, zoom)
    if (width <= MAX_GRID_DIM && height <= MAX_GRID_DIM) return zoom
  }
  return 0
}

/** Grid-aligned bounds of a tile grid (the exact ortho extent of the image). */
export function gridBounds(grid: TileGrid): TileBounds {
  const n = 2 ** grid.zoom
  return {
    minLon: (grid.x0 / n) * 360 - 180,
    maxLon: ((grid.x0 + grid.width) / n) * 360 - 180,
    maxLat: latFromTileY(grid.y0, grid.zoom),
    minLat: latFromTileY(grid.y0 + grid.height, grid.zoom),
  }
}

function tileUrl(x: number, y: number, zoom: number): string {
  return MAP_CONFIG.tileUrls.satellite[0]!.replace("{z}", String(zoom))
    .replace("{y}", String(y))
    .replace("{x}", String(x))
}

function loadTileImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.crossOrigin = "anonymous"
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error(`satellite tile failed to load: ${url}`))
    img.src = url
  })
}

/** Fetches the satellite tiles covering bounds and composites them into a JPEG. */
export async function renderSatelliteTile(bounds: TileBounds): Promise<RenderedSatelliteTile> {
  const maxZoom = MAP_CONFIG.tileMaxZoomSatellite
  const grid = tileGrid(bounds, chooseTileZoom(bounds, maxZoom))
  const width = grid.width * TILE_SIZE
  const height = grid.height * TILE_SIZE

  const canvas = document.createElement("canvas")
  canvas.width = width
  canvas.height = height
  const ctx = canvas.getContext("2d")
  if (!ctx) throw new Error("canvas unsupported")

  const tiles: { img: HTMLImageElement; x: number; y: number }[] = []
  for (let row = 0; row < grid.height; row += 1) {
    for (let col = 0; col < grid.width; col += 1) {
      const url = tileUrl(grid.x0 + col, grid.y0 + row, grid.zoom)
      tiles.push({ img: await loadTileImage(url), x: col * TILE_SIZE, y: row * TILE_SIZE })
    }
  }
  for (const { img, x, y } of tiles) {
    ctx.drawImage(img, x, y)
  }

  const blob = await new Promise<Blob>((resolve, reject) => {
    canvas.toBlob(
      (b) => {
        if (b) resolve(b)
        else reject(new Error("JPEG encoding failed"))
      },
      "image/jpeg",
      0.85,
    )
  })

  return { blob, bounds: gridBounds(grid), width, height }
}
