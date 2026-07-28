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
import { debugError } from "../utils/debug"
import type { NamingPanelFeatureData, LayerEntry, LatLng } from "../types"
import { PHASES } from "../phases"

const DEDUPE_METERS = 3
const ROAD_STEP_METERS = 100

const PANEL_COLORS: Record<string, string> = {}
for (const p of PHASES) {
  if (["districts", "roads", "publicBuildings", "publicSpaces"].includes(p.key)) {
    PANEL_COLORS[p.key] = p.color
  }
}

function nearExisting(from: LatLng): boolean {
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

async function addPanelIfMissing(
  label: string,
  lat: number,
  lng: number,
  color: string,
): Promise<void> {
  if (nearExisting({ lat, lng })) return

  const layerStore = useLayerStore()
  const data: NamingPanelFeatureData = {
    type: "namingPanels",
    label,
    lat,
    lng,
  }

  const layerEntry: LayerEntry = {
    id: `panel_${Date.now()}_${crypto.randomUUID().slice(0, 8)}`,
    dbId: "0",
    data,
    type: "marker",
  }

  layerStore.addFeature("namingPanels", layerEntry)

  const featuresStore = useFeaturesStore()
  featuresStore.add({
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

export async function generateNamingPanels(): Promise<void> {
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const tasks: Promise<void>[] = []

  for (const e of state.districts || []) {
    if (e.data.coordinates) {
      const verts = polygonVertices(e.data.coordinates)
      for (const v of verts) {
        tasks.push(addPanelIfMissing(e.data.label, v.lat, v.lng, PANEL_COLORS.districts))
      }
    }
  }

  for (const e of state.roads || []) {
    if (e.data.coordinates && e.data.coordinates.length >= 2) {
      const stations = roadStations(e.data.coordinates)
      for (const v of stations) {
        tasks.push(addPanelIfMissing(e.data.label, v.lat, v.lng, PANEL_COLORS.roads))
      }
    }
  }

  for (const e of state.publicBuildings || []) {
    if (e.data.coordinates && e.data.coordinates.length > 0) {
      const v = firstVertex(e.data.coordinates)
      if (v) {
        tasks.push(addPanelIfMissing(e.data.label, v.lat, v.lng, PANEL_COLORS.publicBuildings))
      }
    }
  }

  for (const e of state.publicSpaces || []) {
    if (e.data.coordinates && e.data.coordinates.length > 0) {
      const v = firstVertex(e.data.coordinates)
      if (v) {
        tasks.push(addPanelIfMissing(e.data.label, v.lat, v.lng, PANEL_COLORS.publicSpaces))
      }
    }
  }

  await Promise.allSettled(tasks)
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
