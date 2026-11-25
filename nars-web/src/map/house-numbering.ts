// ─── HOUSE NUMBERING SHARED UTILITY ──────────────────────────────────────────
// Single source of truth for house number assignment logic.
// Used by both context-menu.ts (map right-click) and house-entrances.ts (UI).
//
// All main entrances on a road are persisted first (if needed), then renumbered
// together in a single server-side pass sorted by arc-length along the road, so
// insertions/deletions are corrected in one computation.
//
// The left/right side of every entrance is decided by the server in that same
// pass (against the true closest point of the whole road polyline, under the
// road's row lock), and the authoritative side comes back in the response. The
// client never recomputes or PUTs sides itself: a client-side guess could drift
// from what the server numbers by (stale stored side, swallowed PUT failures,
// rate limiting) and silently produce the wrong odd/even parity.

import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import { useFeaturesStore } from "../stores/featuresStore"
import { showToast } from "../lib/toast"
import { t } from "../i18n"
import { apiFetch } from "../api"
import { saveToDatabase } from "./features/feature-persistence"
import { debugError } from "../utils/debug"
import type { MaplibreFeature } from "./core/state"

/** Outcome of a renumbering pass, so callers can report a failed recompute. */
export type NumberingResult = "ok" | "noop" | "failed"

export async function numberOneRoad(roadId: string): Promise<NumberingResult> {
  const featuresStore = useFeaturesStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state

  const roadEntry = (state.roads || []).find((r) => r.dbId === roadId)
  if (!roadEntry?.data.coordinates?.length) {
    showToast(t("alert_ref_road_no_coords"), "error")
    return "failed"
  }

  const allMain = (state.houseEntrances || []).filter(
    (e) => e.data.entranceTypeKey === "main_entrance" && e.data.roadDbId === roadId,
  )

  if (!allMain.length) {
    showToast(t("alert_no_unassigned_entrances"), "info")
    return "noop"
  }

  const [turfHelpers, turfNearest] = await Promise.all([
    import("@turf/helpers"),
    import("@turf/nearest-point-on-line"),
  ])

  const roadLine = turfHelpers.lineString(roadEntry.data.coordinates.map((c) => [c.lng, c.lat]))

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
    return "noop"
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
        return "failed"
      }
      return "noop"
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
      // The server decided (and persisted) the side together with the number.
      const side =
        numbered.side === "left" || numbered.side === "right" ? numbered.side : entry.data.side
      useLayerStore().updateFeature("houseEntrances", entry.dbId, {
        entranceNumber: numbered.entranceNumber,
        label: numbered.label,
        side,
      })
      entry.data.entranceNumber = numbered.entranceNumber
      entry.data.label = numbered.label
      entry.data.side = side
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
    return failureCount > 0 ? "failed" : "ok"
  } catch (err) {
    debugError("setHouseNumbers error:", err)
    showToast(t("map_assigned_numbers_error"), "error")
    return "failed"
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
