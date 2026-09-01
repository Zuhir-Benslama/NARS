// ─── HOUSE NUMBERING SHARED UTILITY ──────────────────────────────────────────
// Single source of truth for house number assignment logic.
// Used by both context-menu.ts (map right-click) and house-entrances.ts (UI).
//
// Projects unassigned entrance markers onto the reference road polyline, sorts
// them by arc-length, then assigns odd numbers to the left side and even to
// the right — each counter continuing from the highest already-assigned number.

import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import { useFeaturesStore } from "../stores/featuresStore"
import { showToast } from "../lib/toast"
import { t } from "../i18n"
import { apiFetch } from "../api"
import { debugError } from "../utils/debug"
import type { MaplibreFeature } from "./core/state"

export async function setHouseNumbers(): Promise<void> {
  const featuresStore = useFeaturesStore()
  const appStore = useAppStore()
  if (appStore.referenceRoadDbId == null) {
    showToast(t("alert_no_ref_road"), "error")
    return
  }

  const layerStore = useLayerStore()
  const state = layerStore.$state
  const roadEntry = (state.roads || []).find((r) => r.dbId === appStore.referenceRoadDbId)
  if (!roadEntry?.data.coordinates?.length) {
    showToast(t("alert_ref_road_no_coords"), "error")
    return
  }

  const unassigned = (state.houseEntrances || []).filter(
    (e) =>
      e.data.entranceTypeKey === "main_entrance" &&
      e.data.roadDbId === appStore.referenceRoadDbId &&
      e.data.label === "?",
  )

  if (!unassigned.length) {
    showToast(t("alert_no_unassigned_entrances"), "info")
    return
  }

  const [turfHelpers, turfNearest] = await Promise.all([
    import("@turf/helpers"),
    import("@turf/nearest-point-on-line"),
  ])

  const roadLine = turfHelpers.lineString(roadEntry.data.coordinates.map((c) => [c.lng, c.lat]))

  const withDist = unassigned
    .filter((e) => e.data.lng != null && e.data.lat != null)
    .map((e) => {
      const lng = e.data.lng!
      const lat = e.data.lat!
      const pt = turfHelpers.point([lng, lat])
      const snapped = turfNearest.default(roadLine, pt, { units: "meters" })
      return { entry: e, dist: snapped.properties.location ?? 0 }
    })
  withDist.sort((a, b) => a.dist - b.dist)

  // Send the batch (ordered by arc length along the road) to a single atomic
  // server-side endpoint. The server locks the road, computes a dense,
  // collision-free odd/even sequence in batch order, persists it, and returns
  // the authoritative numbers. Only those are applied to the store — the UI
  // must not show numbers the server never stored.
  const orderedIds = withDist.map(({ entry }) => entry.dbId)

  try {
    const res = await apiFetch("/api/features/number-entrances", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ roadId: appStore.referenceRoadDbId, entranceIds: orderedIds }),
    })
    if (!res.ok) throw new Error(`House numbering failed (${res.status})`)

    const body = (await res.json()) as {
      success: boolean
      entrances: Array<{ id: string; side: string; entranceNumber: number; label: string }>
    }
    if (!body.success) throw new Error("House numbering failed")

    const byDbId = new Map(body.entrances.map((n) => [n.id, n]))
    let assignedCount = 0
    let failureCount = 0
    const mapPatches: Array<{
      id: string
      properties: MaplibreFeature["properties"]
    }> = []

    for (const { entry } of withDist) {
      const numbered = byDbId.get(entry.dbId)
      if (!numbered) {
        failureCount++
        continue
      }
      useLayerStore().updateFeature("houseEntrances", entry.dbId, {
        entranceNumber: numbered.entranceNumber,
        label: numbered.label,
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
