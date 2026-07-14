// ─── SHARED MAP STATE ─────────────────────────────────────────────────────────

import type maplibregl from "maplibre-gl"
import type { Geoman } from "@geoman-io/maplibre-geoman-free"
import { useFeaturesStore } from "../../stores/featuresStore"

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
  satelliteStyle?: maplibregl.StyleSpecification
  streetStyle?: maplibregl.StyleSpecification
  lightStyle?: maplibregl.StyleSpecification
  darkStyle?: maplibregl.StyleSpecification
}

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

/**
 * Internal setter — called once from initMap() after constructing the ctx object.
 */
export function _setCtx(ctx: MapContext): void {
  _ctx = ctx
}

/**
 * Reset map state (for testing). Clears the ctx so the next getCtx() call
 * will throw "accessed before initMap()" — same as initial state.
 */
export function resetMapState(): void {
  _ctx = null
}

// ─── SELECTION HIGHLIGHT ─────────────────────────────────────────────────────

/**
 * Update the selection highlight layer to show the currently selected feature.
 * Called when the user clicks on a feature or clicks empty space.
 */
export function updateSelectionHighlight(dbId: string | null): void {
  if (!_ctx) return
  const map = _ctx.map
  const source = map?.getSource("selection") as GeoJSONSourceLike | undefined
  if (!source) return

  if (!dbId) {
    source.setData({ type: "FeatureCollection", features: [] })
    return
  }

  // Find the feature in the features store and copy its geometry
  const selected = useFeaturesStore()
    .getAll()
    .find((f: MaplibreFeature) => f.properties?.dbId === dbId)

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
