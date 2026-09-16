// ─── PHASE STORAGE ────────────────────────────────────────────────────
// Persists the current phase PER COMMUNE so a reload restores where the user
// left off daira-by-daira.
//
// IMPORTANT (phase reorder, Sep 2026): earlier builds stored a RAW INTEGER
// index into the phase array. Because phase ORDER can change (phases were
// reordered so that cityCenter/roads come before districts), a stored integer
// index no longer names the same phase after a reorder — the app would resume
// on the WRONG (data-still-empty) phase, i.e. "nothing changed" even though
// the phase list is in the new order.
//
// Fix: persist the stable phase KEY (areas, cityCenter, ...) and migrate any
// legacy raw-integer value on first read.

import { PHASES } from "../phases"

// Order of the phase KEYS as shipped BEFORE the Sep 2026 reorder. Only used as
// the one-time migration table for values persisted as raw integers; the KEY
// names are order-stable, which is what we now store.
const OLD_PHASE_ORDER = [
  "areas",
  "districts",
  "cityCenter",
  "roads",
  "houseEntrances",
  "publicBuildings",
  "publicSpaces",
  "namingPanels",
] as const

export function phaseKeyToIndex(key: string): number | null {
  const idx = PHASES.findIndex((p) => p.key === key)
  return idx === -1 ? null : idx
}

export function getPhaseStorageKey(communeId?: number | string | null): string {
  const base = "nars_current_phase"
  if (communeId === null || communeId === undefined) return base
  return `${base}_${String(communeId)}`
}

function parseLegacy(raw: string): string | null {
  const n = Number.parseInt(raw, 10)
  if (Number.isNaN(n)) return null
  return OLD_PHASE_ORDER[n] ?? null
}

export function savePhase(index: number, communeId?: number | string | null): void {
  if (typeof window === "undefined" || typeof localStorage === "undefined") return
  try {
    const key = getPhaseStorageKey(communeId)
    const phase = PHASES[index]
    // Persist the stable phase KEY; never the raw index.
    localStorage.setItem(key, phase ? phase.key : String(index))
  } catch {
    // Ignore storage errors (e.g. private mode, quota exceeded)
  }
}

export function loadPhase(communeId?: number | string | null): number | null {
  if (typeof window === "undefined" || typeof localStorage === "undefined") return null
  try {
    const key = getPhaseStorageKey(communeId)
    const raw = localStorage.getItem(key)
    if (raw == null) return null

    // New format: the stored value is a phase KEY. Resolve directly.
    const byKey = phaseKeyToIndex(raw)
    if (byKey !== null) return byKey

    // Legacy format: a raw integer index in the OLD phase order. One-time
    // migration: translate old-index → stable key → new index, then upgrade
    // the stored value to the key form so the integer is never read again.
    const legacyKey = parseLegacy(raw)
    if (legacyKey !== null) {
      const newIndex = phaseKeyToIndex(legacyKey)
      if (newIndex !== null) {
        try {
          localStorage.setItem(key, legacyKey)
        } catch {
          // ignore
        }
        return newIndex
      }
    }

    return null
  } catch {
    return null
  }
}
