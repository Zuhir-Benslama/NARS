// ─── FEATURE LABELS ──────────────────────────────────────────────────────────
// Shared localization helpers for feature display labels. Kept in its own
// module (no dependency on draw-save) so both the loader and the draw save
// path can use it without creating a circular import.

import { PHASES } from "../../phases"
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
import type { FeatureData } from "../../types"

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

export function subtypeLabelFor(phaseKey: string, data: FeatureData): string {
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
    if (fr) msgs.push(fr as Record<string, unknown>)
  } catch {}
  try {
    const ar = i18n.global.getLocaleMessage("ar")
    if (ar) msgs.push(ar as Record<string, unknown>)
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

export function displayLabelFor(phase: (typeof PHASES)[number], data: FeatureData): string {
  const typeLabel = subtypeLabelFor(phase.key, data)
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

  const isZoneType =
    phase.key === "districts" &&
    (data.districtTypeKey === "trad_activities_zone" || data.districtTypeKey === "industry_zone")
  const isCityCenter = phase.key === "cityCenter"
  const stripped =
    isZoneType || isCityCenter ? "" : stripLeadingTypePrefix(sanitizeApiText(preferredRaw))

  return stripped ? `${typeLabel} ${stripped}`.trim() : typeLabel
}
