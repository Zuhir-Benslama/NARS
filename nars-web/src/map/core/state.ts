// ─── SHARED MAP STATE ─────────────────────────────────────────────────────────

import type maplibregl from "maplibre-gl"
import type { Geoman } from "@geoman-io/maplibre-geoman-free"
import { debugWarn, debugLog } from "../../utils/debug"

export interface GeoJSONSourceLike {
  setData(data: GeoJSON.FeatureCollection | GeoJSON.Feature): void
}

export const POLYLINE_WIDTH = 8

export interface MapContext {
  map: maplibregl.Map
  geoman?: Geoman
  boundariesSource?: GeoJSONSourceLike
  scatteredSource?: GeoJSONSourceLike
  featuresSource?: GeoJSONSourceLike
  endpointsSource?: GeoJSONSourceLike
  // Cached GeoJSON — restored into fresh sources after a setStyle() wipe
  boundariesGeoJson?: GeoJSON.FeatureCollection
  scatteredGeoJson?: GeoJSON.FeatureCollection
  popup?: maplibregl.Popup
  satelliteStyle?: maplibreStyle
  streetStyle?: maplibreStyle
  lightStyle?: maplibreStyle
  darkStyle?: maplibreStyle
}

type maplibreStyle = maplibregl.StyleSpecification

// Internal mutable state — use getCtx() to access safely.
let _ctx: MapContext | null = null

/**
 * Get the initialized map context. Throws if called before initMap().
 */
export function getCtx(): MapContext {
  if (!_ctx) {
    throw new Error("[NARS] Map context accessed before initMap().")
  }
  return _ctx
}

// Proxy target object — stores the actual data.
const _ctxTarget: MapContext = {} as MapContext

// Reactive proxy for accessing the map context. Guards against access before initMap().
// Use `getCtx()` for type-safe access in new code.
export const ctx: MapContext = new Proxy(_ctxTarget, {
  get(target, prop: keyof MapContext) {
    if (!_ctx) throw new Error(`[NARS] ctx.${String(prop)} accessed before initMap()`)
    return target[prop]
  },
  set(target, prop: keyof MapContext, value) {
    target[prop as keyof MapContext] = value as never
    return true
  },
})

/**
 * Internal setter — called once from initMap() after constructing the ctx object.
 */
export function _setCtx(ctx: MapContext): void {
  // Copy all properties into the Proxy target
  Object.assign(_ctxTarget, ctx)
  _ctx = ctx
}

/**
 * Reset map state (for testing). Clears the ctx so the next getCtx() call
 * will throw "accessed before initMap()" — same as initial state.
 */
export function resetMapState(): void {
  _ctx = null
  for (const key of Object.keys(_ctxTarget) as (keyof MapContext)[]) delete _ctxTarget[key]
}

// ─── FEATURES STORE ───────────────────────────────────────────────────────────
// Single source of truth for all drawn features.
// Keeps the 'nars-features' GeoJSON source in sync via one setData call.
//
// Geoman monitors `sourcedata` events on EVERY source on the map. To prevent
// it from indexing our features (causing "already exists" errors):
//   - We do NOT include an `id` field on GeoJSON features (Geoman indexes by id)
//   - We do NOT include any property that looks like a Geoman ID (__gm_id etc.)
//   - We look up features by properties.dbId and our in-memory array instead

export const featuresStore: {
  features: MaplibreFeature[]
  add: (feature: MaplibreFeature) => void
  batchAdd: (features: MaplibreFeature[]) => void
  clear: () => void
  remove: (id: string) => void
  update: (id: string, patch: Partial<Pick<MaplibreFeature, "geometry" | "properties">>) => void
  getAll: () => MaplibreFeature[]
  updateSource: () => void
} = {
  features: [],

  add(feature: MaplibreFeature) {
    this.features.push(feature)
    this.updateSource()
  },

  // Load all at once — single setData instead of N individual setData calls.
  // After clear() + batchAdd, only ONE updateSource fires here.
  batchAdd(incoming: MaplibreFeature[]) {
    debugLog("[STORE] batchAdd incoming count:", incoming.length)
    this.features.push(...incoming)
    this.updateSource()
  },

  // Reset in-memory store and clear the map source.
  // Call this before every loadFromDatabase() to prevent stale duplicates.
  // Does NOT call updateSource() — the caller (batchAdd) will trigger it once.
  clear() {
    this.features = []
  },

  remove(id: string) {
    this.features = this.features.filter((f) => f.id !== id)
    this.updateSource()
  },

  update(id: string, patch: Partial<Pick<MaplibreFeature, "geometry" | "properties">>) {
    const f = this.features.find((f) => f.id === id)
    if (f) {
      if (patch.geometry) f.geometry = patch.geometry
      if (patch.properties) f.properties = { ...f.properties, ...patch.properties }
      this.updateSource()
    } else {
      debugWarn("featuresStore.update: feature not found", id)
    }
  },

  getAll() {
    return this.features
  },

  updateSource() {
    if (!ctx.featuresSource) {
      debugWarn("updateSource called but ctx.featuresSource is NOT set!")
      return
    }
    const data: GeoJSON.FeatureCollection = {
      type: "FeatureCollection",
      // No `id` field — prevents Geoman from indexing our features
      features: this.features.map((f) => ({
        type: "Feature" as const,
        geometry: f.geometry,
        properties: f.properties,
      })),
    }
    debugLog("[STORE] updateSource - features count:", data.features.length)
    const geomTypes = new Map<string, number>()
    for (const f of data.features) {
      const t = f.geometry?.type ?? "null"
      geomTypes.set(t, (geomTypes.get(t) || 0) + 1)
    }
    debugLog("[STORE] Geometry types:", Object.fromEntries(geomTypes))

    try {
      ctx.featuresSource?.setData(data)
    } catch (err) {
      debugWarn("featuresStore.updateSource failed:", err)
    }
  },
}

// ─── SELECTION HIGHLIGHT ─────────────────────────────────────────────────────

/**
 * Update the selection highlight layer to show the currently selected feature.
 * Called when the user clicks on a feature or clicks empty space.
 */
export function updateSelectionHighlight(dbId: string | null): void {
  const map = ctx.map
  const source = map?.getSource("selection") as GeoJSONSourceLike | undefined
  if (!source) return

  if (!dbId) {
    source.setData({ type: "FeatureCollection", features: [] })
    return
  }

  // Find the feature in the featuresStore and copy its geometry
  const selected = featuresStore.getAll().find((f: MaplibreFeature) => f.properties?.dbId === dbId)

  if (selected && selected.geometry) {
    const feature: GeoJSON.Feature = {
      type: "Feature",
      geometry: selected.geometry,
      properties: {},
    }
    source.setData({ type: "FeatureCollection", features: [feature] })
  } else {
    source.setData({ type: "FeatureCollection", features: [] })
  }
}

export interface MaplibreFeature {
  id: string // in-memory only — never written to GeoJSON output
  geometry: GeoJSON.Geometry
  properties: {
    dbId?: string
    phaseKey: string
    label: string
    geomType?: string
    fillColor?: string
    fillOpacity?: number
    lineColor?: string
    lineWidth?: number
    circleColor?: string
    circleRadius?: number
    textColor?: string
  }
}
