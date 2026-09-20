// ─── DRAFTS API ────────────────────────────────────────────────────────────────
// Typed client for the AI draft-feature lifecycle: list, accept, reject, edit
// geometry and delete. Mirrors DraftFeaturesController routes.

import { apiFetch } from "./index"
import type { components } from "./schema"

type AiDraftFeatureDto = components["schemas"]["AiDraftFeatureDto"]
type PagedResponse = components["schemas"]["PagedResponseOfAiDraftFeatureDto"]

export interface DraftListParams {
  communeId: number | null
  featureType?: "road" | "building"
  status?: "pending" | "accepted" | "rejected" | "edited"
  skip?: number
  take?: number
}
export type DraftFeatureType = "road" | "building"

export async function listDrafts(params: DraftListParams): Promise<AiDraftFeatureDto[]> {
  const query = new URLSearchParams()
  if (params.communeId != null) query.set("communeId", String(params.communeId))
  if (params.featureType) query.set("featureType", params.featureType)
  query.set("status", params.status ?? "pending")
  query.set("skip", String(params.skip ?? 0))
  query.set("take", String(params.take ?? 200))

  const res = await apiFetch(`/api/draft-features?${query.toString()}`)
  const data = (await res.json()) as PagedResponse
  return data.items ?? []
}

export async function acceptDraft(id: string): Promise<void> {
  await apiFetch(`/api/draft-features/${id}/accept`, { method: "POST" })
}

export async function rejectDraft(id: string): Promise<void> {
  await apiFetch(`/api/draft-features/${id}/reject`, { method: "POST" })
}

export async function updateDraftGeometry(id: string, geometryGeoJson: string): Promise<Response> {
  return apiFetch(`/api/draft-features/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ geometryGeoJson }),
  })
}

export async function deleteDraft(id: string): Promise<void> {
  await apiFetch(`/api/draft-features/${id}`, { method: "DELETE" })
}

// ─── AI ROAD GENERATION ──────────────────────────────────────────────────────

// segma inference is CPU-bound and can take tens of seconds, and the
// generate-roads step runs rule checks + DB writes. The apiFetch default
// timeout is only 10s, so these calls opt into a much longer window. A
// commune-scale z18 tile (the resolution that makes roads resolve) is ~5x5
// inference windows ≈ 104s of model time, so this must clear that plus rule
// checks and DB writes.
const ROAD_GENERATION_TIMEOUT_MS = 240_000

export interface TileBounds {
  minLon: number
  minLat: number
  maxLon: number
  maxLat: number
}

/** Mirrors SegmentSummaryResponse (camelCased by the API serializer). */
export interface SegmentSummary {
  buildingCount: number
  roadCount: number
  draftIds: string[]
}

/** POST /api/draft-features/segment — multipart satellite tile → AI drafts. */
export async function segmentTile(params: {
  communeId: number
  tile: Blob
  bounds: TileBounds
}): Promise<SegmentSummary> {
  const form = new FormData()
  form.set("CommuneId", String(params.communeId))
  form.set("Tile", params.tile, "satellite.jpg")
  form.set("FeatureType", "road")
  form.set("MinLon", String(params.bounds.minLon))
  form.set("MinLat", String(params.bounds.minLat))
  form.set("MaxLon", String(params.bounds.maxLon))
  form.set("MaxLat", String(params.bounds.maxLat))

  const res = await apiFetch("/api/draft-features/segment", {
    method: "POST",
    body: form,
    timeout: ROAD_GENERATION_TIMEOUT_MS,
  })
  return (await res.json()) as SegmentSummary
}

export interface GeneratedRoad {
  dbId: string
  layer: string
  label: string
  data: {
    type?: string
    coordinates?: { lat: number; lng: number }[]
  }
}

/** Mirrors GenerateRoadsResponse (camelCased by the API serializer). */
export interface GenerateRoadsResponse {
  dropped: number
  created: GeneratedRoad[]
}

/** POST /api/draft-features/generate-roads — rule-checked auto-save of drafts. */
export async function generateRoadsFromDraftIds(
  communeId: number,
  draftIds: string[],
): Promise<GenerateRoadsResponse> {
  const res = await apiFetch("/api/draft-features/generate-roads", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ communeId, draftIds }),
    timeout: ROAD_GENERATION_TIMEOUT_MS,
  })
  return (await res.json()) as GenerateRoadsResponse
}

export type { AiDraftFeatureDto }
