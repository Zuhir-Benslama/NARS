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

export async function setHouseNumbers(options?: { syncCounts?: boolean }): Promise<void> {
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

  let oddNext = 1,
    evenNext = 2
  ;(state.houseEntrances || [])
    .filter(
      (e) =>
        e.data.entranceTypeKey === "main_entrance" &&
        e.data.roadDbId === appStore.referenceRoadDbId &&
        e.data.label !== "?" &&
        e.data.entranceNumber != null,
    )
    .forEach((e) => {
      const n = e.data.entranceNumber!
      if (n % 2 !== 0 && n >= oddNext) oddNext = n + 2
      if (n % 2 === 0 && n >= evenNext) evenNext = n + 2
    })

  const updates: Promise<void>[] = []

  for (const { entry } of withDist) {
    const isLeft = entry.data.side === "left"
    const number = isLeft ? oddNext : evenNext
    if (isLeft) oddNext += 2
    else evenNext += 2

    entry.data.entranceNumber = number
    entry.data.label = String(number)

    featuresStore.update(entry.id, {
      properties: { phaseKey: entry.data.type, label: String(number) },
    })

    updates.push(
      apiFetch(`/api/features/${entry.dbId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ data: entry.data }),
      }).catch((err) =>
        debugError(`setHouseNumbers save error (id=${entry.dbId}):`, err),
      ) as Promise<void>,
    )
  }

  await Promise.all(updates)

  if (options?.syncCounts !== false) {
    appStore.syncCounts()
  }

  showToast(`Assigned numbers to ${unassigned.length} entrances.`, "success")
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
