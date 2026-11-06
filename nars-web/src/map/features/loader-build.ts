// ─── LOADER BUILD ─────────────────────────────────────────────────────────────
// Builds GeoJSON features from loaded database entries with proper styling.

import { PHASES, CITY_CENTER_COLOR } from "../../phases"
import { CITY_CENTER_CONFIG } from "../../config"
import { featureDataToGeometry } from "./feature-data"
import { getFeatureStyle } from "../draw/draw-save"
import { sanitizeApiText } from "../../utils/sanitize"
import { t } from "../../i18n"
import {
  AREA_TYPES,
  DISTRICT_TYPES,
  ROAD_TYPES,
  PUBLIC_SPACE_TYPES,
  PUBLIC_BUILDING_SECTORS,
} from "../../types/feature-types"
import { debugLog } from "../../utils/debug"
import type { FeatureData, ModalResult } from "../../types"

interface GeoJsonFeatureWithStyle {
  type: "Feature"
  geometry: GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon
  properties: Record<string, unknown>
}

function subtypeKeyFor(phaseKey: string, data: FeatureData): string | undefined {
  switch (phaseKey) {
    case "areas":
      return data.areaTypeKey
    case "districts":
      return data.districtTypeKey
    case "roads":
      return data.roadTypeKey
    case "publicBuildings":
      return data.buildingTypeKey
    case "publicSpaces":
      return data.spaceTypeKey
    default:
      return undefined
  }
}

function subtypeLabelFor(phaseKey: string, data: FeatureData): string {
  const key = subtypeKeyFor(phaseKey, data)
  if (key) {
    const resolved = t(`featureTypes.${key}`)
    if (resolved && resolved !== `featureTypes.${key}`) return resolved
    const all = [
      ...AREA_TYPES,
      ...DISTRICT_TYPES,
      ...ROAD_TYPES,
      ...PUBLIC_SPACE_TYPES,
      ...PUBLIC_BUILDING_SECTORS.flatMap((s) => s.buildings),
    ]
    return all.find((x) => x.key === key)?.label ?? key
  }
  if (phaseKey === "cityCenter") return t("phase_cityCenter_label")
  if (phaseKey === "namingPanels") return t("phase_namingPanels_label")
  return phaseKey
}

export function buildGeoJsonFeature(
  dbId: string,
  data: FeatureData,
  phase: (typeof PHASES)[number],
): GeoJsonFeatureWithStyle | null {
  const style = getFeatureStyle(phase, data as ModalResult)
  const sanitizedLabel = sanitizeApiText(data.label)
  const typeLabel = subtypeLabelFor(phase.key, data)
  // Display label is the (localized) subtype followed by the name. Rows that
  // already carry the prefix are left untouched to avoid double-prefixing.
  const displayLabel =
    sanitizedLabel && !sanitizedLabel.startsWith(`${typeLabel} `)
      ? `${typeLabel} ${sanitizedLabel}`.trim()
      : sanitizedLabel

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
            label: displayLabel,
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
          label: displayLabel,
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
        label: displayLabel,
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
          label: displayLabel,
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
