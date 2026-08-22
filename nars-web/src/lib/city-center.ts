// ─── CITY CENTER RADIUS RULE ──────────────────────────────────────────────────
// One implementation of the min/max radius rule; UI layers map the result to
// their own message style (toast vs inline form error).

import { CITY_CENTER_CONFIG } from "../config"

export type CityCenterRadiusError = "too_small" | "too_large" | null

export function cityCenterRadiusError(radius: number | undefined | null): CityCenterRadiusError {
  if (!radius || Number.isNaN(radius) || radius < CITY_CENTER_CONFIG.minRadiusM) {
    return "too_small"
  }
  if (radius > CITY_CENTER_CONFIG.maxRadiusM) {
    return "too_large"
  }
  return null
}
