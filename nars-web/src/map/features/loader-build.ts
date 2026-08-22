// ─── LOADER BUILD ─────────────────────────────────────────────────────────────
// Builds GeoJSON features from loaded database entries with proper styling.

import { PHASES, CITY_CENTER_COLOR } from "../../phases"
import { CITY_CENTER_CONFIG } from "../../config"
import { featureDataToGeometry } from "./feature-data"
import { getFeatureStyle } from "../draw/draw-save"
import { sanitizeApiText } from "../../utils/sanitize"
import { debugLog } from "../../utils/debug"
import type { FeatureData, ModalResult } from "../../types"

interface GeoJsonFeatureWithStyle {
  type: "Feature"
  geometry: GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon
  properties: Record<string, unknown>
}

export function buildGeoJsonFeature(
  dbId: string,
  data: FeatureData,
  phase: (typeof PHASES)[number],
): GeoJsonFeatureWithStyle | null {
  const style = getFeatureStyle(phase, data as ModalResult)
  const sanitizedLabel = sanitizeApiText(data.label)

  debugLog(
    "[buildGeoJson]",
    phase.key,
    "lat:",
    data.lat,
    "lng:",
    data.lng,
    "coords:",
    data.coordinates?.length ?? "none",
  )

  if (data.lat != null && data.lng != null) {
    if (phase.key === "cityCenter") {
      const radius = data.radius
      if (radius && radius > 0) {
        return {
          type: "Feature" as const,
          geometry: featureDataToGeometry(data, "circle"),
          properties: {
            dbId,
            phaseKey: phase.key,
            label: sanitizedLabel,
            geomType: "LineString",
            lineColor: CITY_CENTER_COLOR,
            lineWidth: CITY_CENTER_CONFIG.ringStrokeWidth,
            radius,
          },
        }
      }
      return {
        type: "Feature" as const,
        geometry: featureDataToGeometry(data, "circle"),
        properties: {
          dbId,
          phaseKey: phase.key,
          label: sanitizedLabel,
          geomType: "Point",
          ...style,
          circleColor: CITY_CENTER_COLOR,
          circleRadius: 12,
          textColor: "#000000",
        },
      }
    }

    return {
      type: "Feature" as const,
      geometry: featureDataToGeometry(data, "marker"),
      properties: {
        dbId,
        phaseKey: phase.key,
        label: sanitizedLabel,
        geomType: "Point",
        ...style,
      },
    }
  } else if (data.coordinates && data.coordinates.length > 0) {
    if (phase.drawType === "polyline") {
      return {
        type: "Feature" as const,
        geometry: featureDataToGeometry(data, "line"),
        properties: {
          dbId,
          phaseKey: phase.key,
          label: sanitizedLabel,
          geomType: "LineString",
          ...style,
        },
      }
    } else {
      return {
        type: "Feature" as const,
        geometry: featureDataToGeometry(data, "polygon"),
        properties: {
          dbId,
          phaseKey: phase.key,
          label: sanitizedLabel,
          geomType: "Polygon",
          ...style,
        },
      }
    }
  }

  debugLog(
    "[LOAD] Skipping feature geometry:",
    data.type,
    "lat:",
    data.lat,
    "lng:",
    data.lng,
    "coords:",
    data.coordinates?.length ?? "none",
  )
  return null
}
