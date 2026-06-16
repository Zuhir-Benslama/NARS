import { apiFetch } from "./index"
import type { paths, components } from "./schema"

// ─── RESPONSE TYPE EXTRACTOR ──────────────────────────────────────────────

type Json<T> = T extends { content: { "application/json": infer J } } ? J : never

// ─── OPTIONS BUILDER ──────────────────────────────────────────────────────

function jsonBody(body: unknown): RequestInit {
  return {
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }
}

// ─── AUTH ─────────────────────────────────────────────────────────────────

export async function signIn(
  body: components["schemas"]["SignInRequest"],
): Promise<Json<paths["/api/signin"]["post"]["responses"][200]>> {
  const res = await apiFetch("/api/signin", { method: "POST", ...jsonBody(body) })
  return res.json()
}

export async function refreshToken(): Promise<
  Json<paths["/api/refresh"]["post"]["responses"][200]>
> {
  const res = await apiFetch("/api/refresh", { method: "POST" })
  return res.json()
}

export async function logout(): Promise<Json<paths["/api/logout"]["post"]["responses"][200]>> {
  const res = await apiFetch("/api/logout", { method: "POST" })
  return res.json()
}

// ─── FEATURES (CRUD) ──────────────────────────────────────────────────────

export async function loadFeatures(
  skip = 0,
  take = 1000,
): Promise<Json<paths["/api/load"]["get"]["responses"][200]>> {
  const res = await apiFetch(`/api/load?skip=${skip}&take=${take}`)
  return res.json()
}

export async function saveFeature(
  body: components["schemas"]["FeatureSaveRequest"],
): Promise<Json<paths["/api/save"]["post"]["responses"][201]>> {
  const res = await apiFetch("/api/save", { method: "POST", ...jsonBody(body) })
  return res.json()
}

export async function updateFeature(
  featureId: string,
  body: components["schemas"]["FeatureUpdateRequest"],
): Promise<Json<paths["/api/update/{featureId}"]["put"]["responses"][200]>> {
  const res = await apiFetch(`/api/update/${featureId}`, { method: "PUT", ...jsonBody(body) })
  return res.json()
}

export async function deleteFeature(
  featureId: string,
): Promise<Json<paths["/api/delete/{featureId}"]["delete"]["responses"][200]>> {
  const res = await apiFetch(`/api/delete/${featureId}`, { method: "DELETE" })
  return res.json()
}

// ─── USER / ACCOUNT ───────────────────────────────────────────────────────

export async function updateUser(
  body: components["schemas"]["UpdateUserRequest"],
): Promise<Json<paths["/api/user/update"]["put"]["responses"][200]>> {
  const res = await apiFetch("/api/user/update", { method: "PUT", ...jsonBody(body) })
  return res.json()
}

// ─── ADMIN ────────────────────────────────────────────────────────────────

export async function createAdminUser(
  body: components["schemas"]["CreateAdminRequest"],
): Promise<Json<paths["/api/admin/users"]["post"]["responses"][201]>> {
  const res = await apiFetch("/api/admin/users", { method: "POST", ...jsonBody(body) })
  return res.json()
}

export async function getAdminOverview(): Promise<
  Json<paths["/api/admin/overview"]["get"]["responses"][200]>
> {
  const res = await apiFetch("/api/admin/overview")
  return res.json()
}

export async function getManageableUsers(): Promise<Record<string, unknown>[]> {
  const res = await apiFetch("/api/admin/users")
  return res.json()
}

export async function updateAdminUser(
  userId: string,
  body: Record<string, unknown>,
): Promise<Response> {
  return apiFetch(`/api/admin/users/${userId}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  })
}

export async function deleteAdminUser(userId: string): Promise<Response> {
  return apiFetch(`/api/admin/users/${userId}`, { method: "DELETE" })
}

export async function getWilayaReport(
  wilayaId: number,
): Promise<Json<paths["/api/admin/wilaya/{wilayaId}"]["get"]["responses"][200]>> {
  const res = await apiFetch(`/api/admin/wilaya/${wilayaId}`)
  return res.json()
}

// ─── LOCATIONS ────────────────────────────────────────────────────────────

export async function getWilayas(
  search?: string,
): Promise<Json<paths["/api/wilayas"]["get"]["responses"][200]>> {
  const qs = search ? `?search=${encodeURIComponent(search)}` : ""
  const res = await apiFetch(`/api/wilayas${qs}`)
  return res.json()
}

export async function getDairas(
  wilayaId: number,
  search?: string,
): Promise<Json<paths["/api/dairas"]["get"]["responses"][200]>> {
  const params: Record<string, string> = { wilaya_id: String(wilayaId) }
  if (search) params.search = search
  const res = await apiFetch(`/api/dairas?${new URLSearchParams(params)}`)
  return res.json()
}

export async function getCommunes(
  dairaId: number,
  search?: string,
): Promise<Json<paths["/api/communes"]["get"]["responses"][200]>> {
  const params: Record<string, string> = { daira_id: String(dairaId) }
  if (search) params.search = search
  const res = await apiFetch(`/api/communes?${new URLSearchParams(params)}`)
  return res.json()
}

export async function getCommuneBoundary(
  communeId: number,
): Promise<Json<paths["/api/commune/{communeId}/boundary"]["get"]["responses"][200]>> {
  const res = await apiFetch(`/api/commune/${communeId}/boundary`)
  return res.json()
}

// ─── VALIDATION ───────────────────────────────────────────────────────────

export async function validateRoad(
  body: components["schemas"]["ValidateRoadRequest"],
): Promise<Json<paths["/api/validate/road"]["post"]["responses"][200]>> {
  const res = await apiFetch("/api/validate/road", { method: "POST", ...jsonBody(body) })
  return res.json()
}

export async function validateDistrict(
  body: components["schemas"]["ValidateDistrictRequest"],
): Promise<Json<paths["/api/validate/district"]["post"]["responses"][200]>> {
  const res = await apiFetch("/api/validate/district", { method: "POST", ...jsonBody(body) })
  return res.json()
}

export async function getDistrictCoverage(): Promise<
  Json<paths["/api/validate/districts/coverage"]["get"]["responses"][200]>
> {
  const res = await apiFetch("/api/validate/districts/coverage")
  return res.json()
}

export async function getMainUrbanExists(): Promise<
  Json<paths["/api/validate/area/main-urban-exists"]["get"]["responses"][200]>
> {
  const res = await apiFetch("/api/validate/area/main-urban-exists")
  return res.json()
}

export async function getRoadSide(
  body: components["schemas"]["RoadSideRequest"],
): Promise<Json<paths["/api/road-side"]["post"]["responses"][200]>> {
  const res = await apiFetch("/api/road-side", { method: "POST", ...jsonBody(body) })
  return res.json()
}

// ─── FIELD ────────────────────────────────────────────────────────────────

export async function getFieldFeatures(
  type?: "road" | "house_entrance" | "naming_panel",
): Promise<Json<paths["/api/field/features"]["get"]["responses"][200]>> {
  const qs = type ? `?type=${type}` : ""
  const res = await apiFetch(`/api/field/features${qs}`)
  return res.json()
}

export async function submitFieldInspection(
  body: components["schemas"]["FieldInspectRequest"],
): Promise<Json<paths["/api/field/inspect"]["post"]["responses"][201]>> {
  const res = await apiFetch("/api/field/inspect", { method: "POST", ...jsonBody(body) })
  return res.json()
}

export async function createFieldEntrance(
  body: components["schemas"]["FieldEntranceCreateRequest"],
): Promise<Json<paths["/api/field/entrance/create"]["post"]["responses"][201]>> {
  const res = await apiFetch("/api/field/entrance/create", { method: "POST", ...jsonBody(body) })
  return res.json()
}

// ─── SPATIAL ──────────────────────────────────────────────────────────────

export async function refreshScattered(): Promise<
  Json<paths["/api/areas/refresh-scattered"]["post"]["responses"][200]>
> {
  const res = await apiFetch("/api/areas/refresh-scattered", { method: "POST" })
  return res.json()
}

// ─── FEATURE TYPES ────────────────────────────────────────────────────────

export async function getFeatureTypes(): Promise<
  Json<paths["/api/feature-types"]["get"]["responses"][200]>
> {
  const res = await apiFetch("/api/feature-types")
  return res.json()
}

export async function createCustomFeatureType(
  body: components["schemas"]["CustomFeatureTypeRequest"],
): Promise<Json<paths["/api/feature-types/custom"]["post"]["responses"][200]>> {
  const res = await apiFetch("/api/feature-types/custom", { method: "POST", ...jsonBody(body) })
  return res.json()
}

// ─── LOGS (raw fetch — no retry, no CSRF) ────────────────────────────────

export async function sendLogs(body: components["schemas"]["LogBatch"]): Promise<void> {
  await fetch("/api/logs", {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  })
}
