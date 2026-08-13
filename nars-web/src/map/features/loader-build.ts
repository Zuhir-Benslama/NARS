// ─── LOADER BUILD ─────────────────────────────────────────────────────────────
// Builds GeoJSON features from loaded database entries with proper styling.

import { PHASES } from "../../phases"
import { computeCircleRing, closeRing } from "../rendering/geometry"
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
        const ring = closeRing(computeCircleRing(data.lat, data.lng, radius))
        return {
          type: "Feature" as const,
          geometry: { type: "LineString" as const, coordinates: ring },
          properties: {
            dbId,
            phaseKey: phase.key,
            label: sanitizedLabel,
            geomType: "LineString",
            lineColor: "#e74c3c",
            lineWidth: 6,
            radius,
          },
        }
      }
      return {
        type: "Feature" as const,
        geometry: { type: "Point" as const, coordinates: [data.lng, data.lat] },
        properties: {
          dbId,
          phaseKey: phase.key,
          label: sanitizedLabel,
          geomType: "Point",
          ...style,
          circleColor: "#e74c3c",
          circleRadius: 12,
          textColor: "#000000",
        },
      }
    }

    return {
      type: "Feature" as const,
      geometry: {
        type: "Point" as const,
        coordinates: [data.lng, data.lat],
      },
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
        geometry: {
          type: "LineString" as const,
          coordinates: data.coordinates.map((c) => [c.lng, c.lat]),
        },
        properties: {
          dbId,
          phaseKey: phase.key,
          label: sanitizedLabel,
          geomType: "LineString",
          ...style,
        },
      }
    } else {
      const ring = data.coordinates.map((c) => [c.lng, c.lat])
      return {
        type: "Feature" as const,
        geometry: {
          type: "Polygon" as const,
          coordinates: [closeRing(ring as [number, number][])],
        },
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
