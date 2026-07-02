// ─── GEOMAN TYPE DECLARATIONS ────────────────────────────────────────────────
// Minimal type declarations for Maplibre-Geoman event payloads and internal
// structures. This reduces the need for `any` across the codebase.
//
// These are intentionally conservative — they describe only the properties
// actually used by NARS, not the full Geoman API surface.

export interface GeomanPointGeometry {
  type: "Point"
  coordinates: [number, number]
  radius?: number
}

export type GeomanGeometry =
  | GeomanPointGeometry
  | (
      | GeoJSON.Point
      | GeoJSON.LineString
      | GeoJSON.Polygon
      | GeoJSON.MultiPoint
      | GeoJSON.MultiLineString
      | GeoJSON.MultiPolygon
    )

// ─── GEOMAN INSTANCE ─────────────────────────────────────────────────────────

export interface GeomanMarker {
  setLngLat(lngLat: [number, number] | { lng: number; lat: number } | maplibregl.LngLat): void
  getLngLat(): { lng: number; lat: number } | maplibregl.LngLat | [number, number]
  toArray?(): [number, number]
  _narsSnapPatchedInstance?: boolean
}

export interface GeomanMarkerPointer {
  marker: {
    setLngLat: (lngLat: [number, number] | { lng: number; lat: number }) => void
    getLngLat: () => { lng: number; lat: number } | [number, number]
    _narsSnapPatchedInstance?: boolean
  } | null
  _narsSnapPatched?: boolean
}

export interface LineDrawer {
  shapeLngLats: [number, number][]
}

export interface ActionInstances {
  draw__polygon?: { lineDrawer?: LineDrawer }
  draw__line?: { lineDrawer?: LineDrawer }
  helper__shape_markers?: {
    sendMarkerRightClickEvent: (featureData: unknown, markerData: unknown) => void
    lineDrawer?: LineDrawer
  }
  [key: string]:
    | {
        lineDrawer?: LineDrawer
        sendMarkerRightClickEvent?: (featureData: unknown, markerData: unknown) => void
      }
    | undefined
}

export interface GeomanFeatureStoreEntry {
  markers?: Map<
    string,
    {
      position?: { coordinate: [number, number] }
      type?: string
    }
  >
}

export interface GeomanFeatures {
  featureStore: Map<string, GeomanFeatureStoreEntry>
}

export interface GeomanInstance {
  map: unknown
  markerPointer?: GeomanMarkerPointer
  actionInstances?: ActionInstances
  features?: GeomanFeatures
  getActiveDrawModes?: () => string[]
  disableDraw: () => Promise<void>
  enableDraw: (shape: string, options?: Record<string, unknown>) => Promise<void>
  toggleControls?: () => void
  removeControls?: () => void
  addControls?: (options?: Record<string, unknown>) => void
}

// ─── GEOMAN EVENTS ───────────────────────────────────────────────────────────

export interface GeomanCreateEvent {
  feature: {
    getGeoJson?: () => GeoJSON.Feature<GeoJSON.Geometry>
    _geoJson?: GeoJSON.Feature<GeoJSON.Geometry>
  }
  shape: string
  layer?: unknown
  featureData?: {
    getGeoJson?: () => GeoJSON.Feature<GeoJSON.Geometry>
    shape?: string
    _geoJson?: GeoJSON.Feature<GeoJSON.Geometry>
  }
}

export interface GeomanEditEvent {
  feature: {
    _geoJson?: GeoJSON.Feature<GeoJSON.Geometry>
  }
  layer?: unknown
  shape?: string
}

export interface GeomanRemoveEvent {
  feature: {
    _geoJson?: GeoJSON.Feature<GeoJSON.Geometry>
  }
  layer?: unknown
}

export interface GeomanMarkerDragEvent {
  markerIndex?: number
  vertexIndex?: number
  layer?: unknown
}

// ─── GEOMAN MAP EVENTS ───────────────────────────────────────────────────────

export interface GeomanMapMouseEvent extends MouseEvent {
  lngLat: { lng: number; lat: number }
  point: { x: number; y: number }
  originalEvent: MouseEvent
}

// ─── GEOMAN INTERNAL LINE DRAWER ──────────────────────────────────────────────
// Extended LineDrawer type covering all properties accessed by removeLastVertex
// and the draw-control modules. These are Geoman internals, not public API.

export interface GeomanDrawMarkerEntry {
  instance?: {
    remove?: () => void
  }
}

export interface GeomanDrawFeatureData {
  markers?: Map<string, GeomanDrawMarkerEntry>
  updateGeometry?: (geometry: GeoJSON.Geometry) => Promise<void> | void
  convertToPolygon?: () => Promise<void> | void
  fireUpdateEvent?: (
    fd: unknown,
    event: {
      type: string
      instance: maplibregl.Marker
      position: {
        coordinate: [number, number]
        path: string[]
      }
    },
  ) => Promise<void> | void
}

export interface GeomanLineDrawer extends LineDrawer {
  featureData?: GeomanDrawFeatureData
  getFeatureGeoJson?: (options?: { withControlMarker?: boolean }) => GeoJSON.Feature
  snappingHelper?: {
    setCustomSnappingCoordinates?: (key: unknown, coords: [number, number][]) => void
  }
  snappingKey?: unknown
  setSnapping?: () => void
  gm?: {
    markerPointer?: {
      marker?: maplibregl.Marker | null
    } | null
  } | null
}

export interface GeomanActionInstance {
  lineDrawer?: GeomanLineDrawer
}

/**
 * Cast a Geoman instance to access internal properties not exposed by the
 * library's public types. Centralizes the single `as unknown as` cast
 * so other modules don't need to repeat it.
 */
export function asGeomanInternal(geoman: unknown): GeomanInstance | null {
  return geoman as unknown as GeomanInstance | null
}

// ─── TYPE GUARD HELPERS ──────────────────────────────────────────────────────

/** Narrow an unknown event to GeomanCreateEvent */
export function isGeomanCreateEvent(e: unknown): e is GeomanCreateEvent {
  return typeof e === "object" && e !== null && "shape" in e
}

/** Narrow an unknown event to GeomanEditEvent */
export function isGeomanEditEvent(e: unknown): e is GeomanEditEvent {
  return typeof e === "object" && e !== null && "feature" in e
}

/** Narrow an unknown event to GeomanRemoveEvent */
export function isGeomanRemoveEvent(e: unknown): e is GeomanRemoveEvent {
  return typeof e === "object" && e !== null && "feature" in e
}

/** Narrow an unknown event to GeomanMarkerDragEvent */
export function isGeomanMarkerDragEvent(e: unknown): e is GeomanMarkerDragEvent {
  return typeof e === "object" && e !== null && ("markerIndex" in e || "vertexIndex" in e)
}

// ─── MODULE AUGMENTATION ─────────────────────────────────────────────────────

declare module "maplibre-gl" {
  interface Map {
    geoman?: GeomanInstance
  }
}
