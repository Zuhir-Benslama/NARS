// ─── LOADER BUILD ─────────────────────────────────────────────────────────────
// Builds GeoJSON features from loaded database entries with proper styling.

import { PHASES, CITY_CENTER_COLOR } from "../../phases"
import { CITY_CENTER_CONFIG } from "../../config"
import { featureDataToGeometry } from "./feature-data"
import { getFeatureStyle } from "../draw/draw-save"
import { sanitizeApiText } from "../../utils/sanitize"
import { t, currentLang, i18n } from "../../i18n"
import enMessages from "../../i18n/en.json"
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
  const phaseLabelKey = `phase_${phaseKey}_label`
  const resolved = t(phaseLabelKey)
  if (resolved && resolved !== phaseLabelKey) return resolved
  return phaseKey
}

// ─── TYPE-PREFIX STRIPPING ────────────────────────────────────────────────────
// Collects every localized subtype/phase label we might have baked into a
// persisted feature label, so stale prefixes can be removed on load.
function collectPrefixes(messages: Record<string, unknown>): string[] {
  const out: string[] = []
  if (messages?.featureTypes) out.push(...(Object.values(messages.featureTypes) as string[]))
  for (const k of Object.keys(messages ?? {})) {
    if (k.startsWith("phase_") && k.endsWith("_label")) out.push(String(messages[k]))
  }
  out.push(
    "areas",
    "cityCenter",
    "roads",
    "districts",
    "houseEntrances",
    "publicBuildings",
    "publicSpaces",
    "namingPanels",
  )
  return out
}

function getAllTypePrefixes(): string[] {
  const msgs: Record<string, unknown>[] = [enMessages as Record<string, unknown>]
  try {
    const fr = i18n.global.getLocaleMessage("fr")
    if (fr) msgs.push(fr)
  } catch {}
  try {
    const ar = i18n.global.getLocaleMessage("ar")
    if (ar) msgs.push(ar)
  } catch {}
  return [...new Set(msgs.flatMap((m) => collectPrefixes(m)))].sort(
    (a, b) => String(b).length - String(a).length,
  )
}

function stripLeadingTypePrefix(label: string): string {
  for (const p of getAllTypePrefixes()) {
    const prefix = String(p)
    if (label === prefix) return ""
    if (label.startsWith(prefix + " ")) return label.slice(prefix.length + 1).trim()
  }
  return label
}

export function buildGeoJsonFeature(
  dbId: string,
  data: FeatureData,
  phase: (typeof PHASES)[number],
): GeoJsonFeatureWithStyle | null {
  const style = getFeatureStyle(phase, data as ModalResult)
  const sanitizedLabelLatin = sanitizeApiText(data.labelLatin)
  const typeLabel = subtypeLabelFor(phase.key, data)

  // Choose the name part based on the active language.
  const preferredRaw =
    currentLang.value === "ar"
      ? data.labelAr && String(data.labelAr).trim() !== ""
        ? data.labelAr
        : data.label
      : currentLang.value === "fr"
        ? data.labelFr && String(data.labelFr).trim() !== ""
          ? data.labelFr
          : data.labelLatin && String(data.labelLatin).trim() !== ""
            ? data.labelLatin
            : data.label
        : data.labelEn && String(data.labelEn).trim() !== ""
          ? data.labelEn
          : data.labelLatin && String(data.labelLatin).trim() !== ""
            ? data.labelLatin
            : data.label

  // Strip a leading type/subtype prefix that may have been baked into an
  // older persisted label so we never render a stale translated prefix (or
  // double it) after a language switch.
  // Zone-type districts and city centers carry no separate name — the subtype
  // label IS the name, so drop any stale (possibly translated) name part.
  const isZoneType =
    phase.key === "districts" &&
    (data.districtTypeKey === "trad_activities_zone" || data.districtTypeKey === "industry_zone")
  const isCityCenter = phase.key === "cityCenter"
  const stripped =
    isZoneType || isCityCenter ? "" : stripLeadingTypePrefix(sanitizeApiText(preferredRaw))

  // Display label is the (localized) subtype followed by the name.
  const displayLabel = stripped ? `${typeLabel} ${stripped}`.trim() : typeLabel

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
            labelLatin: sanitizedLabelLatin,
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
          labelLatin: sanitizedLabelLatin,
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
        labelLatin: sanitizedLabelLatin,
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
