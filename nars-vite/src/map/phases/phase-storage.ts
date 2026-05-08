export function getPhaseStorageKey(communeId?: number | string | null): string {
  const base = "nars_current_phase"
  if (communeId === null || communeId === undefined) return base
  return `${base}_${String(communeId)}`
}

export function savePhase(index: number, communeId?: number | string | null): void {
  if (typeof window === "undefined" || typeof localStorage === "undefined") return
  try {
    const key = getPhaseStorageKey(communeId)
    localStorage.setItem(key, String(index))
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
    const parsed = Number.parseInt(raw, 10)
    return Number.isNaN(parsed) ? null : parsed
  } catch {
    return null
  }
}
