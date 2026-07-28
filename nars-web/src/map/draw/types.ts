// ─── SHARED MAP TYPES ─────────────────────────────────────────────────────────
// Types used across multiple map modules to avoid duplication.

export type LngLatInput = [number, number] | { lng: number; lat: number; toArray?(): [number, number] }

export type SetLngLatFn = (lngLat: LngLatInput) => void
