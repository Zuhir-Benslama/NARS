// ─── SPECIAL HOUSE NUMBERING ──────────────────────────────────────────────────
// Counterclockwise numbering around three special feature kinds:
//   cité       — sequential numbers from a user-picked gate, letter suffixes
//                for secondary entrances of the same building (1/A, 1/B…)
//   roundabout — counterclockwise from north, even outside / odd inside
//   square     — same rule, with the polygon boundary as the reference
//
// The server is authoritative: it re-derives order and inside/outside zone
// from the stored feature geometry while holding a row lock. This module only
// selects the candidate entrances, persists cached ones, sends the batch and
// applies the returned numbers to the stores.

import type { MapMouseEvent } from "maplibre-gl"
import { apiFetch } from "../api"
import { t } from "../i18n"
import { showToast } from "../lib/toast"
import { useFeaturesStore } from "../stores/featuresStore"
import { useLayerStore } from "../stores/layerStore"
import type { FeatureData, FeatureTypeKey, LayerEntry, SpecialNumberingMode } from "../types"
import { roundaboutsSubtype } from "../lib/roundabouts"
import { debugError } from "../utils/debug"
import type { MaplibreFeature } from "./core/state"
import { tryGetCtx } from "./core/state"
import { saveToDatabase } from "./features/feature-persistence"
import { numberOneRoad, type NumberingResult } from "./house-numbering"

const DEFAULT_OUTSIDE_BAND_M = 100
const METERS_PER_DEGREE = 111_320

interface LatLng {
  lat: number
  lng: number
}

interface Candidate {
  entry: LayerEntry
  zone: "inside" | "outside"
}

/** Which special numbering rule applies to a feature, if any. */
export function specialModeForFeature(
  phaseKey: FeatureTypeKey,
  data: FeatureData,
): SpecialNumberingMode | null {
  if (phaseKey === "districts" && data.districtTypeKey === "cite") return "cite"
  if (phaseKey === "roundabouts" && roundaboutsSubtype(data) === "roundabout") return "roundabout"
  if (phaseKey === "publicSpaces" && data.spaceTypeKey === "square") return "square"
  return null
}

function outsideBandM(data: FeatureData): number {
  const band = data.numberingOutsideBandM
  return band != null && Number.isFinite(band) && band > 0 ? band : DEFAULT_OUTSIDE_BAND_M
}

function haversineM(a: LatLng, b: LatLng): number {
  const toRad = (v: number) => (v * Math.PI) / 180
  const dLat = toRad(b.lat - a.lat)
  const dLng = toRad(b.lng - a.lng)
  const lat1 = toRad(a.lat)
  const lat2 = toRad(b.lat)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLng / 2) ** 2
  return 2 * 6_371_000 * Math.asin(Math.min(1, Math.sqrt(h)))
}

function pointInPolygon(point: LatLng, ring: LatLng[]): boolean {
  if (ring.length < 3) return false
  let inside = false
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const yi = ring[i].lat
    const yj = ring[j].lat
    if (yi > point.lat !== yj > point.lat) {
      const edgeLng =
        ring[i].lng + ((ring[j].lng - ring[i].lng) * (point.lat - yi)) / (ring[j].lat - ring[i].lat)
      if (point.lng < edgeLng) inside = !inside
    }
  }
  return inside
}

function distanceToRingM(point: LatLng, ring: LatLng[]): number {
  if (ring.length === 0) return Number.POSITIVE_INFINITY
  const cosLat = Math.cos((point.lat * Math.PI) / 180)
  const xy = (p: LatLng) => ({
    x: (p.lng - point.lng) * cosLat * METERS_PER_DEGREE,
    y: (p.lat - point.lat) * METERS_PER_DEGREE,
  })
  let min = Number.POSITIVE_INFINITY
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const a = xy(ring[j])
    const b = xy(ring[i])
    const dx = b.x - a.x
    const dy = b.y - a.y
    const len2 = dx * dx + dy * dy
    const t = len2 === 0 ? 0 : Math.max(0, Math.min(1, -(a.x * dx + a.y * dy) / len2))
    const px = a.x + t * dx
    const py = a.y + t * dy
    min = Math.min(min, Math.hypot(px, py))
  }
  return min
}

function classify(mode: SpecialNumberingMode, point: LatLng, data: FeatureData): string | null {
  if (mode === "roundabout") {
    const center = { lat: data.lat ?? 0, lng: data.lng ?? 0 }
    const radius = data.radius ?? 0
    const distance = haversineM(point, center)
    if (distance <= radius) return "inside"
    return distance <= radius + outsideBandM(data) ? "outside" : null
  }

  const ring = (data.coordinates ?? []) as LatLng[]
  if (pointInPolygon(point, ring)) return "inside"
  if (mode === "square" && distanceToRingM(point, ring) <= outsideBandM(data)) return "outside"
  return null
}

function collectCandidates(mode: SpecialNumberingMode, featureData: FeatureData): Candidate[] {
  const entrances = useLayerStore().$state.houseEntrances ?? []
  const result: Candidate[] = []
  for (const entry of entrances) {
    const d = entry.data
    if (d.lat == null || d.lng == null) continue
    // Cité numbers buildings (main entrances) and their additional entrances;
    // the parity modes only number main entrances.
    if (mode !== "cite" && d.entranceTypeKey !== "main_entrance") continue
    const zone = classify(mode, { lat: d.lat, lng: d.lng }, featureData)
    if (zone) result.push({ entry, zone: zone as Candidate["zone"] })
  }
  return result
}

function findFeature(featureDbId: string): { phaseKey: FeatureTypeKey; data: FeatureData } | null {
  const state = useLayerStore().$state
  const phases: Array<[FeatureTypeKey, LayerEntry[]]> = [
    ["districts", (state.districts ?? []) as LayerEntry[]],
    ["roundabouts", (state.roundabouts ?? []) as LayerEntry[]],
    ["publicSpaces", (state.publicSpaces ?? []) as LayerEntry[]],
  ]
  for (const [phaseKey, entries] of phases) {
    const found = entries.find((e) => e.dbId === featureDbId)
    if (found) return { phaseKey, data: found.data as FeatureData }
  }
  return null
}

/** Numbers all entrances in scope of one special feature. */
export async function numberSpecialFeature(
  featureDbId: string,
  mode?: SpecialNumberingMode,
  startPoint?: LatLng,
): Promise<NumberingResult> {
  const found = findFeature(featureDbId)
  if (!found) {
    showToast(t("alert_special_feature_not_found"), "error")
    return "failed"
  }

  const resolvedMode = mode ?? specialModeForFeature(found.phaseKey, found.data)
  if (!resolvedMode) {
    showToast(t("alert_special_mode_invalid"), "error")
    return "failed"
  }
  if (resolvedMode === "cite" && !startPoint) {
    showToast(t("alert_cite_gate_required"), "error")
    return "failed"
  }

  const candidates = collectCandidates(resolvedMode, found.data)
  if (candidates.length === 0) {
    showToast(t("alert_no_unassigned_entrances"), "info")
    return "noop"
  }

  const featuresStore = useFeaturesStore()
  const mapPatches: Array<{ id: string; properties: MaplibreFeature["properties"] }> = []
  let failureCount = 0

  try {
    const cached = candidates.filter(({ entry }) => String(entry.dbId).startsWith("local_"))
    if (cached.length) {
      const saved = await Promise.all(cached.map(({ entry }) => saveToDatabase(entry.data)))
      cached.forEach(({ entry }, i) => {
        if (saved[i].ok && saved[i].data?.id) {
          const realId = saved[i].data!.id
          entry.dbId = realId
          featuresStore.update(entry.id, {
            properties: { dbId: realId, phaseKey: entry.data.type },
          })
        } else {
          failureCount++
        }
      })
    }

    const persisted = candidates.filter(({ entry }) => !String(entry.dbId).startsWith("local_"))
    if (persisted.length === 0) {
      showToast(t("map_assigned_numbers_error"), "error")
      return "failed"
    }

    const res = await apiFetch("/api/features/number-entrances-special", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        mode: resolvedMode,
        featureId: featureDbId,
        entranceIds: persisted.map(({ entry }) => entry.dbId),
        startPoint: resolvedMode === "cite" ? startPoint : undefined,
      }),
    })
    if (!res.ok) throw new Error(`Special numbering failed (${res.status})`)

    const body = (await res.json()) as {
      success: boolean
      entrances: Array<{
        id: string
        entranceNumber: number
        label: string
        numberSuffix?: string
      }>
    }
    if (!body.success) throw new Error("Special numbering failed")

    const byDbId = new Map(body.entrances.map((n) => [n.id, n]))
    let assigned = 0
    for (const { entry } of persisted) {
      const numbered = byDbId.get(entry.dbId)
      if (!numbered) {
        failureCount++
        continue
      }
      const patch = {
        entranceNumber: numbered.entranceNumber,
        numberSuffix: numbered.numberSuffix,
        label: numbered.label,
        numberingMode: resolvedMode,
        numberingFeatureId: featureDbId,
        numberingStart: resolvedMode === "cite" ? startPoint : undefined,
      }
      useLayerStore().updateFeature("houseEntrances", entry.dbId, patch)
      Object.assign(entry.data, patch)
      featuresStore.update(entry.id, { properties: { label: numbered.label } })
      mapPatches.push({
        id: entry.id,
        properties: { phaseKey: entry.data.type, label: numbered.label },
      })
      assigned++
    }
    featuresStore.batchUpdate(mapPatches)

    if (failureCount > 0) {
      showToast(t("map_assigned_numbers_partial", { assigned, failed: failureCount }), "error")
      return "failed"
    }
    showToast(t("map_assigned_numbers", { count: assigned }), "success")
    return "ok"
  } catch (err) {
    debugError("special numbering error:", err)
    showToast(t("map_assigned_numbers_error"), "error")
    return "failed"
  }
}

/**
 * Cité numbering starts at a gate picked by the user. Enters a one-shot
 * map-click mode, then numbers counterclockwise from that point.
 */
export function beginCiteGatePick(featureDbId: string): void {
  const ctx = tryGetCtx()
  if (!ctx) {
    showToast(t("alert_special_map_unavailable"), "error")
    return
  }

  showToast(t("map_pick_cite_gate"), "info")
  const canvas = ctx.map.getCanvas()
  const previousCursor = canvas.style.cursor
  canvas.style.cursor = "crosshair"

  const cleanup = () => {
    canvas.style.cursor = previousCursor
    window.removeEventListener("keydown", onKey)
  }
  const onKey = (event: KeyboardEvent) => {
    if (event.key === "Escape") {
      cleanup()
      showToast(t("map_cite_gate_cancelled"), "info")
    }
  }
  const onClick = (event: MapMouseEvent) => {
    cleanup()
    void numberSpecialFeature(featureDbId, "cite", {
      lat: event.lngLat.lat,
      lng: event.lngLat.lng,
    })
  }

  window.addEventListener("keydown", onKey)
  ctx.map.once("click", onClick)
}

/**
 * Shared re-number dispatcher used by delete/move/undo hooks. Special-numbered
 * entrances go back through their own feature; everything else uses the road.
 */
export async function renumberEntranceAfterChange(
  data: Pick<FeatureData, "roadDbId" | "numberingMode" | "numberingFeatureId" | "numberingStart">,
): Promise<NumberingResult> {
  if (data.numberingMode && data.numberingFeatureId) {
    return numberSpecialFeature(
      data.numberingFeatureId,
      data.numberingMode,
      data.numberingStart ?? undefined,
    )
  }
  if (data.roadDbId) return numberOneRoad(data.roadDbId)
  return "noop"
}

/**
 * Re-runs a special feature's numbering after its geometry/info changed. The
 * gate point of a cité is recovered from its numbered entrances so an auto
 * pass does not need the user to pick the gate again.
 */
export async function renumberSpecialFeatureByDbId(featureDbId: string): Promise<NumberingResult> {
  const found = findFeature(featureDbId)
  if (!found) return "noop"
  const mode = specialModeForFeature(found.phaseKey, found.data)
  if (!mode) return "noop"
  const linked = (useLayerStore().$state.houseEntrances ?? []).find(
    (e) => (e.data as FeatureData).numberingFeatureId === featureDbId,
  )
  const start = (linked?.data as FeatureData | undefined)?.numberingStart
  return numberSpecialFeature(featureDbId, mode, start ?? undefined)
}

/** Clears special-numbering metadata when its reference feature is deleted. */
export async function clearSpecialNumberingForFeature(featureDbId: string): Promise<void> {
  const layerStore = useLayerStore()
  const affected = (layerStore.$state.houseEntrances ?? []).filter(
    (e) => (e.data as FeatureData).numberingFeatureId === featureDbId,
  )
  await Promise.all(
    affected.map(async (entry) => {
      const data = { ...entry.data } as FeatureData & Record<string, unknown>
      delete data.numberingMode
      delete data.numberingFeatureId
      delete data.numberingStart
      layerStore.updateFeature("houseEntrances", entry.dbId, {
        numberingMode: undefined,
        numberingFeatureId: undefined,
        numberingStart: undefined,
      })
      Object.assign(entry.data, {
        numberingMode: undefined,
        numberingFeatureId: undefined,
        numberingStart: undefined,
      })
      if (String(entry.dbId).startsWith("local_")) return
      try {
        await apiFetch(`/api/features/${entry.dbId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ data }),
        })
      } catch (err) {
        debugError("failed to clear special numbering metadata:", err)
      }
    }),
  )
}
