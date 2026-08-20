// ─── NAMING PANELS GENERATOR ────────────────────────────────────────────────
// Automatically places naming panel markers derived from existing features.
// Rules:
//  - Districts: every vertex
//  - Roads: start, end, and every 100 meters along the polyline
//  - Public Buildings: first drawn vertex of the polygon
//  - Public Spaces: first drawn vertex of the polygon
//  - Dedupe if a naming panel already exists within 3 meters
//  - Labels are copied from the source feature (no suffixes)

import distance from "@turf/distance"
import { useLayerStore } from "../stores/layerStore"
import { useFeaturesStore } from "../stores/featuresStore"
import { debugError, debugWarn } from "../utils/debug"
import { saveToDatabase } from "./features/feature-persistence"
import type { NamingPanelFeatureData, LayerEntry, LatLng } from "../types"
import type { MaplibreFeature } from "./core/state"
import { PHASES } from "../phases"

const DEDUPE_METERS = 3
const ROAD_STEP_METERS = 100

const PANEL_COLORS: Record<string, string> = {}
for (const p of PHASES) {
  if (["districts", "roads", "publicBuildings", "publicSpaces"].includes(p.key)) {
    PANEL_COLORS[p.key] = p.color
  }
}

// A candidate is a duplicate if it falls within DEDUPE_METERS of a panel that
// already exists in the store OR of one placed earlier in this same run.
function nearExisting(from: LatLng, placed: LatLng[]): boolean {
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const existing = state.namingPanels || []
  for (const e of existing) {
    if (e.data.lat && e.data.lng) {
      const dist = distance([from.lng, from.lat], [e.data.lng, e.data.lat], {
        units: "meters",
      })
      if (dist < DEDUPE_METERS) return true
    }
  }
  for (const p of placed) {
    const dist = distance([from.lng, from.lat], [p.lng, p.lat], {
      units: "meters",
    })
    if (dist < DEDUPE_METERS) return true
  }
  return false
}

function polygonVertices(coords: LatLng[]): LatLng[] {
  if (!coords?.length) return []
  const res: LatLng[] = []
  for (let i = 0; i < coords.length; i++) {
    if (i === coords.length - 1) {
      const first = coords[0]
      const last = coords[coords.length - 1]
      if (first && last && first.lat === last.lat && first.lng === last.lng) break
    }
    res.push(coords[i])
  }
  return res
}

function firstVertex(coords: LatLng[]): LatLng | null {
  return coords?.length ? coords[0] : null
}

function roadStations(coords: LatLng[]): LatLng[] {
  if (!coords || coords.length < 2) return []
  const out: LatLng[] = []

  out.push(coords[0])

  let nextAt = ROAD_STEP_METERS
  let acc = 0
  for (let i = 0; i < coords.length - 1; i++) {
    const a = coords[i]
    const b = coords[i + 1]
    const segLen = distance([a.lng, a.lat], [b.lng, b.lat], {
      units: "meters",
    })
    if (segLen <= 0) continue

    while (acc + segLen >= nextAt) {
      const remain = nextAt - acc
      const t = remain / segLen
      const lat = a.lat + (b.lat - a.lat) * t
      const lng = a.lng + (b.lng - a.lng) * t
      out.push({ lat, lng })
      nextAt += ROAD_STEP_METERS
    }
    acc += segLen
  }

  out.push(coords[coords.length - 1])
  return out
}

export async function generateNamingPanels(): Promise<void> {
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const placed: LatLng[] = []
  const pendingPanels: LayerEntry[] = []
  const maplibreFeatures: MaplibreFeature[] = []

  const place = (label: string, lat: number, lng: number, color: string): void => {
    if (nearExisting({ lat, lng }, placed)) return
    placed.push({ lat, lng })

    const data: NamingPanelFeatureData = {
      type: "namingPanels",
      label,
      lat,
      lng,
    }

    const layerEntry: LayerEntry = {
      id: `panel_${Date.now()}_${crypto.randomUUID().slice(0, 8)}`,
      dbId: `local_${crypto.randomUUID().slice(0, 8)}`,
      data,
      type: "marker",
    }

    pendingPanels.push(layerEntry)
    maplibreFeatures.push({
      id: layerEntry.id,
      geometry: { type: "Point", coordinates: [lng, lat] },
      properties: {
        dbId: layerEntry.dbId,
        phaseKey: "namingPanels",
        label,
        geomType: "Point",
        circleColor: color,
        circleRadius: 6,
        textColor: "#333333",
      },
    })
  }

  for (const e of state.districts || []) {
    if (e.data.coordinates) {
      for (const v of polygonVertices(e.data.coordinates)) {
        place(e.data.label, v.lat, v.lng, PANEL_COLORS.districts)
      }
    }
  }

  for (const e of state.roads || []) {
    if (e.data.coordinates && e.data.coordinates.length >= 2) {
      for (const v of roadStations(e.data.coordinates)) {
        place(e.data.label, v.lat, v.lng, PANEL_COLORS.roads)
      }
    }
  }

  for (const e of state.publicBuildings || []) {
    if (e.data.coordinates && e.data.coordinates.length > 0) {
      const v = firstVertex(e.data.coordinates)
      if (v) place(e.data.label, v.lat, v.lng, PANEL_COLORS.publicBuildings)
    }
  }

  for (const e of state.publicSpaces || []) {
    if (e.data.coordinates && e.data.coordinates.length > 0) {
      const v = firstVertex(e.data.coordinates)
      if (v) place(e.data.label, v.lat, v.lng, PANEL_COLORS.publicSpaces)
    }
  }

  // Persist the panels so they survive reload and get real server dbIds.
  // Persistence failures degrade gracefully: the panel stays local-only.
  if (pendingPanels.length > 0) {
    const outcomes = await Promise.allSettled(
      pendingPanels.map((panel) => saveToDatabase(panel.data)),
    )
    outcomes.forEach((outcome, i) => {
      const panel = pendingPanels[i]
      const result =
        outcome.status === "fulfilled"
          ? outcome.value
          : { ok: false, error: String(outcome.reason) }
      if (result.ok && result.data?.id) {
        panel.dbId = result.data.id
        maplibreFeatures[i].properties.dbId = result.data.id
      } else {
        debugWarn(
          `[PANELS] Panel "${panel.data.label}" not persisted (${result.error}) — kept local-only.`,
        )
      }
    })

    for (const panel of pendingPanels) {
      if (!panel.dbId.startsWith("local_")) {
        layerStore.addFeature("namingPanels", panel)
      }
    }
  }

  // Batch-add only successfully persisted panels so the GeoJSON source is
  // rewritten once instead of once per panel.
  const persistedFeatures = maplibreFeatures.filter(
    (f) => f.properties.dbId && !f.properties.dbId.startsWith("local_"),
  )
  if (persistedFeatures.length > 0) {
    useFeaturesStore().batchAdd(persistedFeatures)
  }
}

if (import.meta.env.DEV) {
  const devFn = async () => {
    try {
      await generateNamingPanels()
    } catch (err) {
      debugError("Generate naming panels error:", err)
    }
  }
  ;(window as unknown as Record<string, unknown>).__narsSetNamingPanels = devFn
}
