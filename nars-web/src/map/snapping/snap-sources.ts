// ─── SNAP SOURCE COLLECTORS ───────────────────────────────────────────────────
// Collect snap geometry from all relevant sources.
// Each accepts an optional excludeId; if omitted falls back to the shared
// snapExclude managed by snapping.ts.

import { getCtx } from "../core/state"
import { useLayerStore } from "../../stores/layerStore"
import { useSnapStore } from "../../stores/snapStore"
import type { LayerState } from "../../stores/layerStore"

/** Called by snapping.ts when the exclude target changes. */
export function setSnapSourceExclude(id: string | null): void {
  useSnapStore().snapExclude = id
}

function resolveExclude(excludeId?: string | null): string | null {
  return excludeId ?? useSnapStore().snapExclude
}

/** Rings from polygon feature layers (areas, districts, publicBuildings, publicSpaces) */
export function getSnapRings(
  phaseKeys: string[],
  excludeId?: string | null,
): Array<{ lat: number; lng: number }[]> {
  const exclude = resolveExclude(excludeId)
  const rings: Array<{ lat: number; lng: number }[]> = []
  const layerStore = useLayerStore()
  const state = layerStore.$state
  for (const key of phaseKeys) {
    const entries = state[key as keyof LayerState]
    if (!entries) continue
    for (const entry of entries) {
      if (entry.id === exclude) continue
      if (entry.type !== "polygon") continue
      const coords = entry.data.coordinates
      if (!coords || coords.length < 3) continue
      rings.push(coords)
    }
  }

  // Commune boundary rings (matching reference: boundariesLayer)
  const boundariesGeoJson = getCtx().boundariesGeoJson
  if (boundariesGeoJson) {
    const extractRings = (coords: unknown): void => {
      if (!Array.isArray(coords) || coords.length === 0) return
      const head = (coords as unknown[])[0]
      const isLinearRing =
        Array.isArray(head) &&
        head.length >= 2 &&
        typeof head[0] === "number" &&
        typeof head[1] === "number"
      if (isLinearRing) {
        const ring = (coords as [number, number][]).map(([lng, lat]) => ({
          lat,
          lng,
        }))
        if (ring.length >= 3) rings.push(ring)
        return
      }
      for (const part of coords as unknown[]) extractRings(part)
    }
    for (const feature of boundariesGeoJson.features) {
      const geom = feature.geometry
      if (geom.type === "Polygon") {
        geom.coordinates.forEach((ring: unknown) => extractRings(ring))
      } else if (geom.type === "MultiPolygon") {
        geom.coordinates.forEach((poly: unknown) =>
          (poly as unknown[]).forEach((ring: unknown) => extractRings(ring)),
        )
      }
    }
  }

  return rings
}

/** Road chains from roads layer */
export function getRoadChains(
  phaseKeys: string[],
  excludeId?: string | null,
): Array<{ lat: number; lng: number }[]> {
  const exclude = resolveExclude(excludeId)
  const chains: Array<{ lat: number; lng: number }>[] = []
  if (!phaseKeys.includes("roads")) return chains
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const entries = state.roads
  if (!entries) return chains
  for (const entry of entries) {
    if (entry.id === exclude) continue
    if (entry.type !== "line") continue
    const coords = entry.data.coordinates
    if (!coords || coords.length < 2) continue
    chains.push(coords)
  }
  return chains
}

/** City center points with radius info */
export function getCityCenterCircles(
  phaseKeys: string[],
  excludeId?: string | null,
): Array<{ lat: number; lng: number; radius: number }> {
  const exclude = resolveExclude(excludeId)
  const circles: Array<{ lat: number; lng: number; radius: number }> = []
  if (!phaseKeys.includes("cityCenter")) return circles
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const entries = state.cityCenter
  if (!entries) return circles
  for (const entry of entries) {
    if (entry.id === exclude) continue
    const d = entry.data
    if (d.lat != null && d.lng != null && d.radius != null && d.radius > 0) {
      circles.push({ lat: d.lat, lng: d.lng, radius: d.radius })
    }
  }
  return circles
}

/** Point features (city centers, etc.) */
export function getSnapPoints(
  phaseKeys: string[],
  excludeId?: string | null,
): Array<{ lat: number; lng: number }> {
  const exclude = resolveExclude(excludeId)
  const points: Array<{ lat: number; lng: number }> = []
  const layerStore = useLayerStore()
  const state = layerStore.$state
  for (const key of phaseKeys) {
    const entries = state[key as keyof LayerState]
    if (!entries) continue
    for (const entry of entries) {
      if (entry.id === exclude) continue
      const d = entry.data as { lat?: number; lng?: number }
      if (d.lat != null && d.lng != null) {
        points.push({ lat: d.lat, lng: d.lng })
      }
    }
  }
  return points
}
