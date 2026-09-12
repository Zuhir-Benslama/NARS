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

export type { AiDraftFeatureDto }
