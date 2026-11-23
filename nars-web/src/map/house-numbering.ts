// ─── HOUSE NUMBERING SHARED UTILITY ──────────────────────────────────────────
// Single source of truth for house number assignment logic.
// Used by both context-menu.ts (map right-click) and house-entrances.ts (UI).
//
// All main entrances on a road are persisted first (if needed), then renumbered
// together in a single server-side pass sorted by arc-length along the road, so
// insertions/deletions are corrected in one computation.

import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import { useFeaturesStore } from "../stores/featuresStore"
import { showToast } from "../lib/toast"
import { t } from "../i18n"
import { apiFetch } from "../api"
import { saveToDatabase } from "./features/feature-persistence"
import { debugError } from "../utils/debug"
import type { MaplibreFeature } from "./core/state"

// Client-side side determination mirroring GeometryHelper.DetermineSideOnPolyline.
function determineSide(
  markerLat: number,
  markerLng: number,
  roadCoords: Array<{ lat: number; lng: number }>,
): "left" | "right" {
  if (roadCoords.length < 2) return "left"
  const cosLat = Math.cos((markerLat * Math.PI) / 180)
  const toXy = (c: { lat: number; lng: number }) => ({
    x: (c.lng - markerLng) * cosLat,
    y: c.lat - markerLat,
  })

  const eps = 1e-12
  let bestDist = Infinity
  let bestIdx = 0
  let bestT = 0

  for (let i = 0; i < roadCoords.length - 1; i++) {
    const a = toXy(roadCoords[i])
    const b = toXy(roadCoords[i + 1])
    const dx = b.x - a.x
    const dy = b.y - a.y
    const len2 = dx * dx + dy * dy
    if (len2 < eps * eps) continue
    const t = Math.max(0, Math.min(1, -(a.x * dx + a.y * dy) / len2))
    const px = a.x + t * dx
    const py = a.y + t * dy
    const d = px * px + py * py
    if (d < bestDist - eps) {
      bestDist = d
      bestIdx = i
      bestT = t
    }
  }

  const s = toXy(roadCoords[bestIdx])
  const e = toXy(roadCoords[bestIdx + 1])
  const leftNormal = (dx: number, dy: number) => {
    const len = Math.sqrt(dx * dx + dy * dy)
    return len < 1e-12 ? { x: 0, y: 0 } : { x: -dy / len, y: dx / len }
  }
  let n = leftNormal(e.x - s.x, e.y - s.y)
  if (bestT <= 0 && bestIdx > 0) {
    const p = toXy(roadCoords[bestIdx - 1])
    const pn = leftNormal(s.x - p.x, s.y - p.y)
    n = { x: n.x + pn.x, y: n.y + pn.y }
  } else if (bestT >= 1 && bestIdx + 2 < roadCoords.length) {
    const nx = toXy(roadCoords[bestIdx + 2])
    const nn = leftNormal(nx.x - e.x, nx.y - e.y)
    n = { x: n.x + nn.x, y: n.y + nn.y }
  }

  const qx = -(s.x + bestT * (e.x - s.x))
  const qy = -(s.y + bestT * (e.y - s.y))
  return n.x * qx + n.y * qy >= 0 ? "left" : "right"
}

export async function numberOneRoad(roadId: string): Promise<void> {
  const featuresStore = useFeaturesStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state

  const roadEntry = (state.roads || []).find((r) => r.dbId === roadId)
  if (!roadEntry?.data.coordinates?.length) {
    showToast(t("alert_ref_road_no_coords"), "error")
    return
  }

  const allMain = (state.houseEntrances || []).filter(
    (e) => e.data.entranceTypeKey === "main_entrance" && e.data.roadDbId === roadId,
  )

  if (!allMain.length) {
    showToast(t("alert_no_unassigned_entrances"), "info")
    return
  }

  const [turfHelpers, turfNearest] = await Promise.all([
    import("@turf/helpers"),
    import("@turf/nearest-point-on-line"),
  ])

  const roadLine = turfHelpers.lineString(roadEntry.data.coordinates.map((c) => [c.lng, c.lat]))

  // Recompute the side (left/right) for every entrance from its current
  // position using the faithful (single-segment) determination, so numbering
  // (odd=left, even=right) uses the correct side instead of a stale value.
  // Done client-side to avoid one API call per entrance (rate limiting).
  for (const e of allMain) {
    if (e.data.lat == null || e.data.lng == null) continue
    const coords = roadEntry.data.coordinates
    if (!coords || coords.length < 2) continue
    const side = determineSide(e.data.lat, e.data.lng, coords)
    if (side !== e.data.side) {
      e.data.side = side
      useLayerStore().updateFeature("houseEntrances", e.dbId, { side })
      // Persist the corrected side for already-stored entrances.
      if (!String(e.dbId).startsWith("local_")) {
        await apiFetch(`/api/features/${e.dbId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ data: e.data }),
        }).catch(() => {})
      }
    }
  }

  const withDist = allMain
    .filter((e) => e.data.lng != null && e.data.lat != null)
    .map((e) => {
      const lng = e.data.lng!
      const lat = e.data.lat!
      const pt = turfHelpers.point([lng, lat])
      const snapped = turfNearest.default(roadLine, pt, { units: "meters" })
      return { entry: e, dist: snapped.properties.location ?? 0 }
    })
  withDist.sort((a, b) => a.dist - b.dist)

  if (!withDist.length) {
    showToast(t("alert_no_unassigned_entrances"), "info")
    return
  }

  let failureCount = 0
  const mapPatches: Array<{
    id: string
    properties: MaplibreFeature["properties"]
  }> = []

  try {
    // Persist any cached (local) entrances first so they receive real ids and
    // can participate in the single spatial renumbering pass below.
    const cachedEntries = withDist.filter(({ entry }) => String(entry.dbId).startsWith("local_"))
    if (cachedEntries.length) {
      const saveResults = await Promise.all(
        cachedEntries.map(({ entry }) => saveToDatabase(entry.data)),
      )
      cachedEntries.forEach(({ entry }, i) => {
        const saveRes = saveResults[i]
        if (saveRes.ok && saveRes.data?.id) {
          const realId = saveRes.data.id
          entry.dbId = realId
          featuresStore.update(entry.id, {
            properties: { dbId: realId, phaseKey: entry.data.type },
          })
        } else {
          failureCount++
        }
      })
    }

    // After persistence, all remaining entrances are persisted (real ids).
    const persistedEntries = withDist.filter(
      ({ entry }) => !String(entry.dbId).startsWith("local_"),
    )

    if (!persistedEntries.length) {
      if (failureCount > 0) {
        showToast(t("map_assigned_numbers_error"), "error")
      }
      return
    }

    const orderedIds = persistedEntries.map(({ entry }) => entry.dbId)
    const res = await apiFetch("/api/features/number-entrances", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ roadId, entranceIds: orderedIds }),
    })
    if (!res.ok) throw new Error(`House numbering failed (${res.status})`)

    const body = (await res.json()) as {
      success: boolean
      entrances: Array<{ id: string; side: string; entranceNumber: number; label: string }>
    }
    if (!body.success) throw new Error("House numbering failed")

    const byDbId = new Map(body.entrances.map((n) => [n.id, n]))
    let assignedCount = 0
    for (const { entry } of persistedEntries) {
      const numbered = byDbId.get(entry.dbId)
      if (!numbered) {
        failureCount++
        continue
      }
      useLayerStore().updateFeature("houseEntrances", entry.dbId, {
        entranceNumber: numbered.entranceNumber,
        label: numbered.label,
      })
      entry.data.entranceNumber = numbered.entranceNumber
      entry.data.label = numbered.label
      featuresStore.update(entry.id, {
        properties: { label: numbered.label },
      })
      mapPatches.push({
        id: entry.id,
        properties: { phaseKey: entry.data.type, label: numbered.label },
      })
      assignedCount++
    }

    featuresStore.batchUpdate(mapPatches)

    if (failureCount > 0) {
      showToast(
        t("map_assigned_numbers_partial", { assigned: assignedCount, failed: failureCount }),
        "error",
      )
    } else {
      showToast(t("map_assigned_numbers", { count: assignedCount }), "success")
    }
  } catch (err) {
    debugError("setHouseNumbers error:", err)
    showToast(t("map_assigned_numbers_error"), "error")
  }
}

export async function setHouseNumbers(allRoads = false): Promise<void> {
  const appStore = useAppStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state

  if (allRoads) {
    const roadIds = new Set(
      (state.houseEntrances || [])
        .filter((e) => e.data.entranceTypeKey === "main_entrance" && e.data.roadDbId)
        .map((e) => e.data.roadDbId as string),
    )
    if (roadIds.size === 0) {
      showToast(t("alert_no_unassigned_entrances"), "info")
      return
    }
    for (const rid of roadIds) {
      await numberOneRoad(rid)
    }
    return
  }

  if (appStore.referenceRoadDbId == null) {
    showToast(t("alert_no_ref_road"), "error")
    return
  }
  await numberOneRoad(appStore.referenceRoadDbId)
}

// ─── FEATURE TYPE MAPPING ─────────────────────────────────────────────────────
// Maps drawType strings to internal geometry type keys.
// Shared between draw-events.ts and loader.ts.

export function getFeatureType(drawType: string): "polygon" | "line" | "circle" | "marker" {
  if (drawType === "polygon") return "polygon"
  if (drawType === "polyline") return "line"
  if (drawType === "line") return "line" // Geoman's shape name for polyline
  if (drawType === "circle") return "circle"
  return "marker"
}
