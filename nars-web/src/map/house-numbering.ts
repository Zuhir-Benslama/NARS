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
import { saveToDatabase } from "./features/feature-persistence"
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

  const cachedEntries = withDist.filter(({ entry }) => String(entry.dbId).startsWith("local_"))
  const persistedEntries = withDist.filter(({ entry }) => !String(entry.dbId).startsWith("local_"))

  let assignedCount = 0
  let failureCount = 0
  const mapPatches: Array<{
    id: string
    properties: MaplibreFeature["properties"]
  }> = []

  try {
    // Persisted (already in the DB) entrances keep using the atomic
    // server-side numbering endpoint, which locks the road and persists the
    // computed numbers.
    if (persistedEntries.length) {
      const orderedIds = persistedEntries.map(({ entry }) => entry.dbId)
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
        mapPatches.push({
          id: entry.id,
          properties: { phaseKey: entry.data.type, label: numbered.label },
        })
        assignedCount++
      }
    }

    // Cached (not yet persisted) entrances: compute their numbers locally,
    // then save them to the database only now that their numbers are known,
    // reducing the number of database transfers.
    if (cachedEntries.length) {
      const usedBySide: Record<string, Set<number>> = { left: new Set(), right: new Set() }
      for (const e of state.houseEntrances || []) {
        if (e.data.roadDbId !== appStore.referenceRoadDbId) continue
        const side = e.data.side
        if (side !== "left" && side !== "right") continue
        let num = e.data.entranceNumber
        if (num == null) {
          const parsed = Number.parseInt(String(e.data.label), 10)
          if (Number.isFinite(parsed)) num = parsed
        }
        if (num != null) usedBySide[side].add(num)
      }

      // Compute all numbers first (no DB access), so the saves below can run
      // in parallel instead of serially.
      const toSave: Array<{
        entry: (typeof cachedEntries)[number]["entry"]
        num: number
        label: string
      }> = []
      for (const { entry } of cachedEntries) {
        const side =
          entry.data.side === "left" || entry.data.side === "right" ? entry.data.side : "left"
        const used = usedBySide[side]
        let suggested = side === "left" ? 1 : 2
        while (suggested <= 100000 && used.has(suggested)) suggested += 2
        const num = suggested > 100000 ? -1 : suggested
        if (num < 0) {
          failureCount++
          continue
        }
        used.add(num)
        const label = String(num)

        useLayerStore().updateFeature("houseEntrances", entry.dbId, {
          entranceNumber: num,
          label,
        })
        entry.data.entranceNumber = num
        entry.data.label = label
        toSave.push({ entry, num, label })
      }

      const saveResults = await Promise.all(toSave.map(({ entry }) => saveToDatabase(entry.data)))
      toSave.forEach(({ entry, label }, i) => {
        const saveRes = saveResults[i]
        if (saveRes.ok && saveRes.data?.id) {
          const realId = saveRes.data.id
          entry.dbId = realId
          featuresStore.update(entry.id, {
            properties: { dbId: realId, phaseKey: entry.data.type, label },
          })
          mapPatches.push({
            id: entry.id,
            properties: { phaseKey: entry.data.type, label },
          })
          assignedCount++
        } else {
          failureCount++
        }
      })
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
