// ─── STYLES, ICONS ───────────────────────────────────────────────────────────

import { AREA_TYPES } from "../../phases"

// ─── POLYGON STYLES ───────────────────────────────────────────────────────────

export function areaStyle(areaTypeKey: string): {
  fillColor: string
  fillOpacity: number
  lineColor: string
  lineWidth: number
} {
  const at = AREA_TYPES.find((a) => a.key === areaTypeKey) ?? AREA_TYPES[0]
  return {
    fillColor: at.color,
    fillOpacity: 0,
    lineColor: at.color,
    lineWidth: 2.5,
  }
}
