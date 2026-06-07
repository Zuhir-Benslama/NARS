// ─── STYLES, ICONS, POPUP ────────────────────────────────────────────────────

import { AREA_TYPES, PHASES } from "../../phases"
import type { FeatureData } from "../../types"
import { t } from "../../i18n"
import { sanitizeText } from "../../utils/sanitize"

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

export const polygonStyles: Record<
  string,
  {
    fillColor: string
    fillOpacity: number
    lineColor: string
    lineWidth: number
  }
> = {
  districts: {
    lineColor: "#f39c12",
    lineWidth: 3,
    fillColor: "#f39c12",
    fillOpacity: 0,
  },
  publicBuildings: {
    lineColor: "#e67e22",
    lineWidth: 3,
    fillColor: "#e67e22",
    fillOpacity: 0.25,
  },
  publicSpaces: {
    lineColor: "#2ecc71",
    lineWidth: 3,
    fillColor: "#2ecc71",
    fillOpacity: 0.2,
  },
}

// ─── ICONS ────────────────────────────────────────────────────────────────────

export function createEntranceIconHtml(label: string | number, color = "#27ae60"): string {
  const text =
    sanitizeText(
      String(label ?? "")
        .trim()
        .slice(0, 6),
    ) || "?"
  const w = text.length <= 2 ? 16 : text.length <= 4 ? 22 : 28
  return `
        <div class="entrance-marker" style="
            display: inline-flex;
            align-items: center;
            justify-content: center;
            background: ${color};
            color: white;
            width: ${w}px;
            height: 16px;
            border-radius: 8px;
            font-size: 10px;
            font-weight: bold;
            border: 2px solid white;
            box-shadow: 0 2px 4px rgba(0,0,0,0.3);
        ">${text}</div>
    `
}

// ─── POPUP BUILDER ────────────────────────────────────────────────────────────

export function buildPopupContent(data: FeatureData, phase: (typeof PHASES)[number]): string {
  const lines = [`<b>${data.label}</b>`, `<small>${t(phase.label)}</small>`]
  if (data.decisionNumber)
    lines.push(`<small>${t("popup_decision")}: ${data.decisionNumber}</small>`)
  if (data.decisionDate) lines.push(`<small>${t("popup_date")}: ${data.decisionDate}</small>`)
  if (data.roadLabel) lines.push(`<small>${t("popup_road")}: ${data.roadLabel}</small>`)
  if (data.side)
    lines.push(
      `<small>${t("popup_side")}: ${data.side} (${t(data.side === "left" ? "popup_side_odd" : "popup_side_even")})</small>`,
    )
  if (data.mainEntranceLabel)
    lines.push(`<small>${t("popup_main_entrance")}: ${data.mainEntranceLabel}</small>`)
  return lines.join("<br>")
}
