// ─── DRAFT STYLE ───────────────────────────────────────────────────────────────
// Visual identity for AI draft features. Drafts are shown with dashed strokes so
// they read as "not yet committed" against the solid user-drawn/committed
// layers. Roads reuse the road blue hue, building drafts a raspberry that is
// distinct from the 8 palette hues used for committed features.

import type { DraftFeatureType } from "../../api/drafts"

export const DRAFT_ROAD_COLOR = "#48c9f0"
export const DRAFT_BUILDING_COLOR = "#c44569"
export const DRAFT_LINE_WIDTH = 6
export const DRAFT_DASH = [4, 3]

export function draftColor(featureType: DraftFeatureType): string {
  return featureType === "road" ? DRAFT_ROAD_COLOR : DRAFT_BUILDING_COLOR
}

export interface DraftStyle {
  lineColor: string
  lineWidth: number
  fillColor?: string
  fillOpacity?: number
  confidence: number
  draftId: string
  featureType: DraftFeatureType
}

export interface DraftGeoJsonFeature {
  type: "Feature"
  geometry: GeoJSON.LineString | GeoJSON.Polygon
  properties: {
    draftId: string
    featureType: DraftFeatureType
    confidence: number
    geomType: "LineString" | "Polygon"
    lineColor: string
    lineWidth: number
    fillColor?: string
    fillOpacity?: number
  }
}

/**
 * Parses the raw GeoJSON string stored on a draft and produces a GeoJSON
 * Feature carrying the dashed draft style plus review metadata. Returns null if
 * the geometry is missing or not LineString/Polygon (the API refuses such
 * drafts, so this is a defensive fallback).
 */
export function parseDraftGeometry(
  draftId: string,
  featureType: DraftFeatureType,
  geometryGeoJson: string,
  confidence: number,
): DraftGeoJsonFeature | null {
  let geometry: GeoJSON.Geometry
  try {
    geometry = JSON.parse(geometryGeoJson) as GeoJSON.Geometry
  } catch {
    return null
  }
  if (geometry.type !== "LineString" && geometry.type !== "Polygon") return null

  const color = draftColor(featureType)
  return {
    type: "Feature",
    geometry,
    properties: {
      draftId,
      featureType,
      confidence,
      geomType: geometry.type,
      lineColor: color,
      lineWidth: DRAFT_LINE_WIDTH,
      ...(geometry.type === "Polygon" ? { fillColor: color, fillOpacity: 0.08 } : {}),
    },
  }
}
