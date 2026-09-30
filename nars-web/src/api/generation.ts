// ─── GENERATION JOBS API ───────────────────────────────────────────────────
// Typed client for the async road-generation queue (GenerationJobController):
// create a job with a full grid split, upload each rendered raster chunk, then
// poll until done and read the GenerateRoadsResponse-shaped result.

import { apiFetch } from "./index"
import type { GenerateRoadsResponse } from "./drafts"
import type { TileBounds } from "./drafts"

/** Mirrors GenerationGridDto (camelCased by the API serializer). */
export interface GenerationGridDto {
  chunkKey: string
  zoom: number
  x0: number
  y0: number
  width: number
  height: number
  minLon: number
  minLat: number
  maxLon: number
  maxLat: number
}

/** Mirrors GenerationChunkView (camelCased by the API serializer). */
export interface GenerationChunkView {
  id: string
  chunkKey: string
  zoom: number
  x0: number
  y0: number
  width: number
  height: number
  minLon: number | null
  minLat: number | null
  maxLon: number | null
  maxLat: number | null
  status: string
  attempts: number
  error: string | null
  createdAt: string
  updatedAt: string | null
}

/** Mirrors GenerationJobView (camelCased by the API serializer). */
export interface GenerationJobView {
  id: string
  communeId: number
  status: string
  stage: string | null
  totalChunks: number
  doneChunks: number
  progress: number
  draftIds: string[]
  error: string | null
  createdAt: string
  updatedAt: string | null
  chunks: GenerationChunkView[]
  result: GenerateRoadsResponse | null
}

export const GENERATION_JOB_STATUS = {
  pending: "pending",
  active: "active",
  accepting: "accepting",
  done: "done",
  failed: "failed",
  cancelled: "cancelled",
} as const

/** POST /api/generation/jobs — registers a job with its full grid split. */
export async function createGenerationJob(
  communeId: number,
  grids: GenerationGridDto[],
): Promise<GenerationJobView> {
  const res = await apiFetch("/api/generation/jobs", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ communeId, grids }),
  })
  return (await res.json()) as GenerationJobView
}

/** GET /api/generation/jobs/{id} — current progress and, once done, result. */
export async function getGenerationJob(id: string): Promise<GenerationJobView> {
  const res = await apiFetch(`/api/generation/jobs/${id}`)
  return (await res.json()) as GenerationJobView
}

/**
 * POST /api/generation/jobs/{id}/chunks/{chunkId}/raster — uploads one
 * rendered chunk. A raster is accepted exactly once per chunk (409 on retry).
 * Uploads are small JPEGs, but keep a generous timeout so the request is not
 * abort-churned on throttled networks while a big commune uploads.
 */
export async function uploadGenerationChunkRaster(
  jobId: string,
  chunkId: string,
  tile: Blob,
  fileName: string,
): Promise<GenerationJobView> {
  const form = new FormData()
  form.set("Raster", tile, fileName)
  const res = await apiFetch(`/api/generation/jobs/${jobId}/chunks/${chunkId}/raster`, {
    method: "POST",
    body: form,
    timeout: 240_000,
  })
  return (await res.json()) as GenerationJobView
}

/** POST /api/generation/jobs/{id}/cancel — stops processing open chunks. */
export async function cancelGenerationJob(jobId: string): Promise<GenerationJobView> {
  const res = await apiFetch(`/api/generation/jobs/${jobId}/cancel`, { method: "POST" })
  return (await res.json()) as GenerationJobView
}

/** Map a grid to the wire shape the API expects (adds the surrounding bounds). */
export function toGenerationGrid(
  chunkKey: string,
  zoom: number,
  grid: { x0: number; y0: number; width: number; height: number },
  bounds: TileBounds,
): GenerationGridDto {
  return {
    chunkKey,
    zoom,
    x0: grid.x0,
    y0: grid.y0,
    width: grid.width,
    height: grid.height,
    minLon: bounds.minLon,
    minLat: bounds.minLat,
    maxLon: bounds.maxLon,
    maxLat: bounds.maxLat,
  }
}
