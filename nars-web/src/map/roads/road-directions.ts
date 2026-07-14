// ─── ROAD DIRECTION COMPUTATION (ORCHESTRATOR) ────────────────────────────────
// Two-phase algorithm:
//   Phase 1 — CONNECTION SCAN: builds full topology graph (road-graph.ts).
//   Phase 2 — ORIENTATION: DFS from city center outward (road-orient.ts).
//   Phase 3 — DEAD-END CORRECTION: overrides vote-based orientation.
//   Phase 4 — APPLY: persist reversals, update markers (road-markers.ts).

import { apiFetch } from "../../api"
import { useLayerStore } from "../../stores/layerStore"
import { useFeaturesStore } from "../../stores/featuresStore"
import { showToast } from "../../lib/toast"
import { debugError } from "../../utils/debug"
import type { LayerEntry } from "../../types"

import { buildConnectionGraph, dm, fromNk } from "./road-graph"
import { geographicDirection, orientFromCityCenter } from "./road-orient"
import { updateEndpointMarkers } from "./road-markers"

// ─── MAIN ENTRY POINT ─────────────────────────────────────────────────────────

export async function computeAndApplyRoadDirections(): Promise<void> {
  const featuresStore = useFeaturesStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const roads = state.roads || []
  if (!roads.length) return

  const { graph, segs } = buildConnectionGraph(roads)
  const visited = new Set<string>()

  const cityCenters = (state.cityCenter || []).filter(
    (e) => e.data.lat != null && e.data.lng != null,
  )

  if (cityCenters.length > 0) {
    for (const ccEntry of cityCenters) {
      const { lat, lng, radius } = ccEntry.data
      orientFromCityCenter({ lat: lat!, lng: lng! }, radius ?? 50, graph, segs, visited)
    }
  } else {
    for (const seg of segs.values()) geographicDirection(seg)
  }

  // ── Phase 3: reconcile segments → roads (majority vote per road) ─────────
  const votes = new Map<
    string,
    { fwd: number; rev: number; entry: LayerEntry; needsReverse?: boolean }
  >()
  for (const seg of segs.values()) {
    const v = votes.get(seg.dbId) ?? { fwd: 0, rev: 0, entry: seg.entry }
    seg.reversed ? v.rev++ : v.fwd++
    votes.set(seg.dbId, v)
  }

  // ── Dead-end correction (on whole roads, after vote) ────────────────────
  for (const vote of votes.values()) {
    const coords = vote.entry.data.coordinates!
    if (!coords?.length) continue

    const first = coords[0]
    const last = coords[coords.length - 1]

    const nodeFirst = [...graph.nodes()].find((k) => dm(fromNk(k), first) <= 30)
    const nodeLast = [...graph.nodes()].find((k) => dm(fromNk(k), last) <= 30)

    if (!nodeFirst || !nodeLast) continue

    const degFirst = graph.degree(nodeFirst)
    const degLast = graph.degree(nodeLast)

    if (degFirst !== 1 && degLast !== 1) continue

    const fromIsFirst = (() => {
      if (degFirst === 1 && degLast === 1) {
        const ccEntries = (state.cityCenter || []).filter(
          (e) => e.data.lat != null && e.data.lng != null,
        )
        if (!ccEntries.length) return degFirst >= degLast
        const cc = { lat: ccEntries[0].data.lat!, lng: ccEntries[0].data.lng! }
        return dm(first, cc) <= dm(last, cc)
      }
      return degFirst > degLast
    })()
    vote.needsReverse = !fromIsFirst
  }

  // ── Phase 4: apply reversals, persist, refresh arrows ────────────────────
  for (const [dbId, { fwd, rev, entry, needsReverse }] of votes) {
    const shouldReverse = needsReverse !== undefined ? needsReverse : rev > fwd
    if (shouldReverse) {
      const reversed = [...entry.data.coordinates!].reverse()
      featuresStore.update(entry.id, {
        geometry: {
          type: "LineString" as const,
          coordinates: reversed.map((c) => [c.lng, c.lat]),
        },
      })
      try {
        await apiFetch(`/api/features/${entry.dbId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ data: entry.data }),
        })
        entry.data.coordinates = reversed
      } catch (err) {
        debugError(`Road direction save error (id=${dbId}):`, err)
        featuresStore.update(entry.id, {
          geometry: {
            type: "LineString" as const,
            coordinates: entry.data.coordinates!.map((c) => [c.lng, c.lat]),
          },
        })
      }
    }
  }

  updateEndpointMarkers()
  showToast(`Road directions applied to ${votes.size} roads.`, "success")
}

export { updateEndpointMarkers } from "./road-markers"
