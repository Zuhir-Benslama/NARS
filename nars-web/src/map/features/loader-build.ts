// ─── LOADER BUILD ─────────────────────────────────────────────────────────────
// Builds GeoJSON features from loaded database entries with proper styling.

import { PHASES, ROUNDABOUTS_COLOR } from "../../phases"
import { ROUNDABOUTS_CONFIG } from "../../config"
import { featureDataToGeometry, roundaboutsGeometryKind } from "./feature-data"
import { displayLabelFor } from "./feature-labels"
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
  const sanitizedLabelLatin = sanitizeApiText(data.labelLatin)
  const displayLabel = displayLabelFor(phase, data)

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
    if (phase.key === "roundabouts") {
      const radius = data.radius
      if (radius && radius > 0) {
        const geometryKind = roundaboutsGeometryKind(data)
        const isFilled = geometryKind === "filledCircle"
        return {
          type: "Feature" as const,
          geometry: featureDataToGeometry(data, geometryKind),
          properties: {
            dbId,
            phaseKey: phase.key,
            label: displayLabel,
            labelLatin: sanitizedLabelLatin,
            geomType: isFilled ? "Polygon" : "LineString",
            ...(isFilled
              ? {
                  fillColor: ROUNDABOUTS_COLOR,
                  fillOpacity: ROUNDABOUTS_CONFIG.cityCenterFillOpacity,
                }
              : {}),
            lineColor: ROUNDABOUTS_COLOR,
            lineWidth: ROUNDABOUTS_CONFIG.ringStrokeWidth,
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
          label: displayLabel,
          labelLatin: sanitizedLabelLatin,
          geomType: "Point",
          ...style,
          circleColor: ROUNDABOUTS_COLOR,
          circleRadius: 12,
          textColor: "#000000",
        },
      }
    }

    const pointProperties: Record<string, unknown> = {
      dbId,
      phaseKey: phase.key,
      label: displayLabel,
      labelLatin: sanitizedLabelLatin,
      geomType: "Point",
      ...style,
    }
    // Ensure house-entrance markers always render their circle even if the
    // style lookup missed (e.g. cached features), falling back to defaults.
    if (phase.key === "houseEntrances") {
      if (pointProperties.circleColor == null) pointProperties.circleColor = phase.color
      if (pointProperties.circleRadius == null) pointProperties.circleRadius = 10
      if (pointProperties.textColor == null) pointProperties.textColor = "#000000"
    }
    return {
      type: "Feature" as const,
      geometry: featureDataToGeometry(data, "marker"),
      properties: pointProperties,
    }
  } else if (data.coordinates && data.coordinates.length > 0) {
    if (phase.drawType === "polyline") {
      return {
        type: "Feature" as const,
        geometry: featureDataToGeometry(data, "line"),
        properties: {
          dbId,
          phaseKey: phase.key,
          label: displayLabel,
          labelLatin: sanitizedLabelLatin,
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
          label: displayLabel,
          labelLatin: sanitizedLabelLatin,
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
