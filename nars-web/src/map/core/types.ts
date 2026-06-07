// ─── MAP & GEOMAN TYPE DEFINITIONS ────────────────────────────────────────────
// Type-safe definitions for Maplibre GL JS events and Geoman interactions.

import type maplibregl from "maplibre-gl"
import type { Geoman } from "@geoman-io/maplibre-geoman-free"

// ─── MAPLIBRE EVENT TYPES ─────────────────────────────────────────────────────

export interface MapClickEvent {
  type: "click"
  target: maplibregl.Map
  lngLat: maplibregl.LngLat
  point: maplibregl.PointLike
  originalEvent?: MouseEvent
}

export interface MapMouseEvent {
  type: string
  target: maplibregl.Map
  lngLat: maplibregl.LngLat
  point: maplibregl.PointLike
  originalEvent?: MouseEvent
  preventDefault?: () => void
}

export interface MapContextMenuEvent extends MapMouseEvent {
  type: "contextmenu"
  originalEvent: MouseEvent
}

export interface MapDoubleClickEvent extends MapMouseEvent {
  type: "dblclick"
  originalEvent: MouseEvent
  preventDefault: () => void
}

// ─── GEOMAN EVENT TYPES ───────────────────────────────────────────────────────

export interface GeomanFeature {
  _geoJson: GeoJSON.Feature
  _layer: Record<string, unknown>
  id?: string | number

  delete: () => Promise<void>
}

export interface GeomanEditEvent {
  type: "gm:editend"
  target: maplibregl.Map
  feature: GeomanFeature
  layer: Record<string, unknown>
  shape: string
}

export interface GeomanRemoveEvent {
  type: "gm:remove"
  target: maplibregl.Map
  feature: GeomanFeature
  layer: Record<string, unknown>
  shape: string
}

export interface GeomanDrawStartEvent {
  type: "pm:create"
  target: maplibregl.Map
  shape: string
  workingLayer: Record<string, unknown>
}

export interface GeomanDrawEndEvent {
  type: "pm:remove"
  target: maplibregl.Map
  layer: Record<string, unknown>
  shape: string
}

// ─── GEOMAN INSTANCE TYPE ─────────────────────────────────────────────────────

export type { Geoman }

// ─── GEOJSON UTILITY TYPES ────────────────────────────────────────────────────

export type GeoJsonCoordinates2D = [number, number]

export type GeoJsonRing = GeoJsonCoordinates2D[]

export type GeoJsonPolygonCoordinates = GeoJsonRing[]

export interface GeoJsonPolygon {
  type: "Polygon"
  coordinates: GeoJsonPolygonCoordinates
}

export interface GeoJsonLineString {
  type: "LineString"
  coordinates: GeoJsonCoordinates2D[]
}

export interface GeoJsonPoint {
  type: "Point"
  coordinates: GeoJsonCoordinates2D
}

export type GeoJsonGeometry = GeoJsonPolygon | GeoJsonLineString | GeoJsonPoint

// ─── SNAP STATE ───────────────────────────────────────────────────────────────

export interface SnapState {
  enabled: boolean
  snapLatLng: { lat: number; lng: number } | null
  excludeId: string | null
}

// ─── DRAWING STATE ────────────────────────────────────────────────────────────

export type DrawMode = "polygon" | "polyline" | "marker" | "circle" | null

export interface DrawingState {
  isDrawing: boolean
  drawType: DrawMode
  currentGeometry: GeoJsonCoordinates2D[]
  vertexMarkers: maplibregl.Marker[]
}

// ─── EDIT STATE ───────────────────────────────────────────────────────────────

export interface EditState {
  isEditMode: boolean
  activeGeomanFeature: GeomanFeature | null
  activeEditEntryId: string | null
}

// ─── MAP CONTEXT TYPE ─────────────────────────────────────────────────────────

export interface MapContext {
  map: maplibregl.Map
  geoman?: Geoman
  satelliteStyle?: maplibregl.StyleSpecification
  streetStyle?: maplibregl.StyleSpecification
  lightStyle?: maplibregl.StyleSpecification
  darkStyle?: maplibregl.StyleSpecification
}
