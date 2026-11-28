// ─── ROAD DIRECTION COMPUTATION (ORCHESTRATOR) ────────────────────────────────
// Two-phase algorithm:
//   Phase 1 — CONNECTION SCAN: builds full topology graph (road-graph.ts).
//   Phase 2 — ORIENTATION: closest-first BFS from roundabouts outward (road-orient.ts).
//   Phase 3 — DEAD-END CORRECTION: overrides vote-based orientation.
//   Phase 4 — APPLY: persist reversals, update markers (road-markers.ts).
//
// Each urban area is processed independently: roads (and roundabouts) are
// grouped by the urban area they fall inside, and the topology graph, the
// orientation DFS and the dead-end correction are run per group. This keeps a
// roundabouts feature in one urban area from orienting roads that belong to another.
// Inside an urban area with a city centre, orientation walks primary roads first,
// then secondary, then tertiary; distance to the centre breaks ties.

import { apiFetch } from "../../api"
import { useLayerStore } from "../../stores/layerStore"
import { useFeaturesStore } from "../../stores/featuresStore"

import { showToast } from "../../lib/toast"
import { roundaboutsSubtype } from "../../lib/roundabouts"
import { debugError } from "../../utils/debug"
import type { LayerEntry } from "../../types"
import type { AreaFeatureData, RoundaboutsFeatureData, RoadFeatureData } from "../../types"
import { t } from "../../i18n"

import { buildConnectionGraph, dm, fromNk, CONNECT_M } from "./road-graph"
const DEAD_END_SNAP_THRESHOLD_M = CONNECT_M
import { geographicDirection, orientFromRoundabouts } from "./road-orient"
import { updateEndpointMarkers } from "./road-markers"
import { numberOneRoad } from "../house-numbering"

const URBAN_AREA_TYPES: ReadonlySet<string> = new Set(["central_urban", "secondary_urban"])

interface Coord {
  lat: number
  lng: number
}

interface RoadGroup {
  area: LayerEntry<AreaFeatureData> | null
  roads: LayerEntry<RoadFeatureData>[]
  roundabouts: LayerEntry<RoundaboutsFeatureData>[]
}

function isUrbanArea(entry: LayerEntry): entry is LayerEntry<AreaFeatureData> {
  return URBAN_AREA_TYPES.has((entry.data as { areaTypeKey?: string }).areaTypeKey ?? "")
}

function pointInArea(area: LayerEntry<AreaFeatureData>, lat: number, lng: number): boolean {
  const ring = area.data.coordinates ?? []
  if (ring.length < 3) return false
  let inside = false
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const xi = ring[i].lat
    const yi = ring[i].lng
    const xj = ring[j].lat
    const yj = ring[j].lng
    if (yi > lng !== yj > lng && lat < ((xj - xi) * (lng - yi)) / (yj - yi) + xi) {
      inside = !inside
    }
  }
  return inside
}

function areaCentroid(area: LayerEntry<AreaFeatureData>): Coord {
  const coords = area.data.coordinates ?? []
  if (!coords.length) return { lat: 0, lng: 0 }
  let lat = 0
  let lng = 0
  for (const c of coords) {
    lat += c.lat
    lng += c.lng
  }
  return { lat: lat / coords.length, lng: lng / coords.length }
}

function euclidSq(a: Coord, b: Coord): number {
  const dLat = a.lat - b.lat
  const dLng = a.lng - b.lng
  return dLat * dLat + dLng * dLng
}

function roadMidpoint(road: LayerEntry<RoadFeatureData>): Coord | null {
  const coords = road.data.coordinates
  if (!coords?.length) return null
  return coords[Math.floor(coords.length / 2)]
}

function buildGroups(
  roads: LayerEntry<RoadFeatureData>[],
  roundabouts: LayerEntry<RoundaboutsFeatureData>[],
  urbanAreas: LayerEntry<AreaFeatureData>[],
): RoadGroup[] {
  if (urbanAreas.length === 0) {
    return [{ area: null, roads: [...roads], roundabouts: [...roundabouts] }]
  }

  const groups: RoadGroup[] = urbanAreas.map((area) => ({
    area,
    roads: [],
    roundabouts: [],
  }))

  // Assign each road to the urban area whose polygon contains its midpoint;
  // fall back to the nearest area when no polygon contains it (e.g. a road
  // that straddles a boundary).
  for (const road of roads) {
    const mid = roadMidpoint(road)
    if (!mid) continue
    let target = groups.find((g) => g.area !== null && pointInArea(g.area, mid.lat, mid.lng))
    if (!target) {
      let bestDist = Infinity
      for (const g of groups) {
        if (!g.area) continue
        const d = euclidSq(mid, areaCentroid(g.area))
        if (d < bestDist) {
          bestDist = d
          target = g
        }
      }
    }
    if (target) target.roads.push(road)
  }

  // Assign each roundabouts feature to the urban area whose polygon contains it.
  for (const cc of roundabouts) {
    const lat = cc.data.lat
    const lng = cc.data.lng
    if (lat == null || lng == null) continue
    let target = groups.find((g) => g.area !== null && pointInArea(g.area, lat, lng))
    if (!target) {
      let bestDist = Infinity
      for (const g of groups) {
        if (!g.area) continue
        const d = euclidSq({ lat, lng }, areaCentroid(g.area))
        if (d < bestDist) {
          bestDist = d
          target = g
        }
      }
    }
    if (target) target.roundabouts.push(cc)
  }

  return groups
}

// ─── MAIN ENTRY POINT ─────────────────────────────────────────────────────────
export async function computeAndApplyRoadDirections(): Promise<void> {
  const featuresStore = useFeaturesStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const roads = state.roads || []
  if (!roads.length) return

  const roundabouts = (state.roundabouts || []).filter(
    (e) => e.data.lat != null && e.data.lng != null,
  )

  const urbanAreas = (state.areas || []).filter(isUrbanArea)

  const groups = buildGroups(
    roads as LayerEntry<RoadFeatureData>[],
    roundabouts as LayerEntry<RoundaboutsFeatureData>[],
    urbanAreas as LayerEntry<AreaFeatureData>[],
  )

  const votes = new Map<
    string,
    { fwd: number; rev: number; entry: LayerEntry; needsReverse?: boolean }
  >()

  for (const group of groups) {
    if (!group.roads.length) continue

    const { graph, segs } = buildConnectionGraph(group.roads as LayerEntry[])

    const visited = new Set<string>()

    const groupRoundabouts = group.roundabouts.filter(
      (e) => e.data.lat != null && e.data.lng != null,
    )

    // Only city centres inside a real urban area impose road-category priority.
    // Plain roundabouts (and groups without an urban area) keep the old
    // distance-only traversal.
    const prioritizeCategories =
      group.area !== null &&
      groupRoundabouts.some((cc) => roundaboutsSubtype(cc.data) === "city_center")

    if (groupRoundabouts.length > 0) {
      for (const ccEntry of groupRoundabouts) {
        const { lat, lng, radius } = ccEntry.data
        if (lat == null || lng == null) continue
        orientFromRoundabouts(
          { lat, lng },
          radius ?? 50,
          graph,
          segs,
          visited,
          prioritizeCategories,
        )
      }
    } else {
      for (const seg of segs.values()) geographicDirection(seg)
    }

    // ── Phase 3: reconcile segments → roads (majority vote per road) ─────
    const groupRoadIds = new Set(group.roads.map((r) => r.dbId))
    for (const seg of segs.values()) {
      const v = votes.get(seg.dbId) ?? { fwd: 0, rev: 0, entry: seg.entry }
      seg.reversed ? v.rev++ : v.fwd++
      votes.set(seg.dbId, v)
    }

    // ── Dead-end correction (on whole roads, after vote) ─────────────────
    for (const vote of votes.values()) {
      if (!groupRoadIds.has(vote.entry.dbId)) continue
      const coords = vote.entry.data.coordinates
      if (!coords?.length) continue

      const first = coords[0]
      const last = coords[coords.length - 1]

      const nodeFirst = [...graph.nodes()].find(
        (k) => dm(fromNk(k), first) <= DEAD_END_SNAP_THRESHOLD_M,
      )
      const nodeLast = [...graph.nodes()].find(
        (k) => dm(fromNk(k), last) <= DEAD_END_SNAP_THRESHOLD_M,
      )

      if (!nodeFirst || !nodeLast) continue

      const degFirst = graph.degree(nodeFirst)
      const degLast = graph.degree(nodeLast)

      if (degFirst !== 1 && degLast !== 1) continue

      // Pick the roundabouts feature belonging to this urban area that is closest to
      // the road, so each urban area stays independent.
      const ccEntries = groupRoundabouts.length ? groupRoundabouts : roundabouts
      const fromIsFirst = (() => {
        if (degFirst === 1 && degLast === 1) {
          if (!ccEntries.length) return degFirst >= degLast
          // nearest roundabouts feature to the road
          let nearest = ccEntries[0]
          let best = Infinity
          const mid = coords[Math.floor(coords.length / 2)]
          for (const cc of ccEntries) {
            const d = dm(mid, { lat: cc.data.lat!, lng: cc.data.lng! })
            if (d < best) {
              best = d
              nearest = cc
            }
          }
          const cc = { lat: nearest.data.lat!, lng: nearest.data.lng! }
          return dm(first, cc) <= dm(last, cc)
        }
        return degFirst > degLast
      })()
      vote.needsReverse = !fromIsFirst
    }
  }

  // ── Phase 4: apply reversals, persist, refresh arrows ────────────────────
  let reversedCount = 0
  let failureCount = 0
  const saveTasks: Promise<void>[] = []
  const mapPatches: Array<{
    id: string
    geometry: GeoJSON.LineString
  }> = []

  for (const [dbId, { fwd, rev, entry, needsReverse }] of votes) {
    const shouldReverse = needsReverse !== undefined ? needsReverse : rev > fwd
    if (!shouldReverse) continue

    const coords = entry.data.coordinates
    if (!coords?.length) continue
    const reversed = [...coords].reverse()

    saveTasks.push(
      apiFetch(`/api/features/${entry.dbId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ data: { ...entry.data, coordinates: reversed } }),
      })
        .then(() => {
          useLayerStore().updateFeature("roads", entry.dbId, { coordinates: reversed })
          mapPatches.push({
            id: entry.id,
            geometry: {
              type: "LineString" as const,
              coordinates: reversed.map((c) => [c.lng, c.lat]),
            },
          })

          // Reversing the road flips the left/right side of every house
          // entrance on it. Sides are decided by the server against the road's
          // new orientation during this renumbering pass, so there is no
          // client-side flip (and no racing per-entrance PUTs that could
          // overwrite the freshly assigned numbers).
          void numberOneRoad(dbId)

          reversedCount++
        })
        .catch((err) => {
          debugError(`Road direction save error (id=${dbId}):`, err)
          failureCount++
        }),
    )
  }

  await Promise.all(saveTasks)
  featuresStore.batchUpdate(mapPatches)

  updateEndpointMarkers()
  if (failureCount > 0) {
    showToast(
      t("map_road_directions_partial", { count: reversedCount, failed: failureCount }),
      "error",
    )
  } else {
    showToast(t("map_road_directions_applied", { count: reversedCount }), "success")
  }
}

export { updateEndpointMarkers } from "./road-markers"
