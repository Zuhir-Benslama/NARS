// ─── FEATURES STORE ───────────────────────────────────────────────────────────
// Pinia store replacing the plain-object featuresStore in state.ts.
// Single source of truth for all drawn features.
// Keeps the 'nars-features' GeoJSON source in sync via one setData call.
//
// Geoman monitors `sourcedata` events on EVERY source on the map. To prevent
// it from indexing our features (causing "already exists" errors):
//   - We do NOT include an `id` field on GeoJSON features (Geoman indexes by id)
//   - We do NOT include any property that looks like a Geoman ID (__gm_id etc.)
//   - We look up features by properties.dbId and our in-memory array instead

import { defineStore } from "pinia"
import { getCtx, type MaplibreFeature } from "../map/core/state"
import { debugWarn, debugLog } from "../utils/debug"

export const useFeaturesStore = defineStore("features", {
  state: () => ({
    features: [] as MaplibreFeature[],
  }),

  actions: {
    add(feature: MaplibreFeature) {
      this.features.push(feature)
      this.updateSource()
    },

    // Load all at once — single setData instead of N individual setData calls.
    // After clear() + batchAdd, only ONE updateSource fires here.
    batchAdd(incoming: MaplibreFeature[]) {
      debugLog("[STORE] batchAdd incoming count:", incoming.length)
      this.features.push(...incoming)
      this.updateSource()
    },

    // Reset in-memory store.
    // Call this before every loadFromDatabase() to prevent stale duplicates.
    // Does NOT call updateSource() — the caller (batchAdd) will trigger it once.
    clear() {
      this.features = []
    },

    remove(id: string) {
      this.features = this.features.filter((f) => f.id !== id)
      this.updateSource()
    },

    update(
      id: string,
      patch: Partial<Pick<MaplibreFeature, "geometry">> & {
        properties?: Partial<MaplibreFeature["properties"]>
      },
    ) {
      const f = this.features.find((f) => f.id === id)
      if (f) {
        if (patch.geometry) f.geometry = patch.geometry
        if (patch.properties) f.properties = { ...f.properties, ...patch.properties }
        this.updateSource()
      } else {
        debugWarn("featuresStore.update: feature not found", id)
      }
    },

    // Apply N patches then call setData once — avoids O(n²) full-source
    // rebuilds when many features change at once (house numbering, road
    // direction reversals).
    batchUpdate(
      patches: Array<{
        id: string
        geometry?: MaplibreFeature["geometry"]
        properties?: Partial<MaplibreFeature["properties"]>
      }>,
    ) {
      for (const patch of patches) {
        const f = this.features.find((f) => f.id === patch.id)
        if (!f) {
          debugWarn("featuresStore.batchUpdate: feature not found", patch.id)
          continue
        }
        if (patch.geometry) f.geometry = patch.geometry
        if (patch.properties) f.properties = { ...f.properties, ...patch.properties }
      }
      this.updateSource()
    },

    getAll(): MaplibreFeature[] {
      return this.features
    },

    updateSource() {
      const ctx = getCtx()
      if (!ctx?.featuresSource) {
        debugWarn("updateSource called but ctx.featuresSource is NOT set!")
        return
      }
      const data: GeoJSON.FeatureCollection = {
        type: "FeatureCollection",
        // No `id` field — prevents Geoman from indexing our features
        features: this.features.map((f) => ({
          type: "Feature" as const,
          geometry: f.geometry,
          properties: f.properties,
        })),
      }
      if (import.meta.env.DEV) {
        debugLog("[STORE] updateSource - features count:", data.features.length)
        const geomTypes = new Map<string, number>()
        for (const f of data.features) {
          const t = f.geometry?.type ?? "null"
          geomTypes.set(t, (geomTypes.get(t) || 0) + 1)
        }
        debugLog("[STORE] Geometry types:", Object.fromEntries(geomTypes))
      }

      try {
        ctx.featuresSource?.setData(data)
      } catch (err) {
        debugWarn("featuresStore.updateSource failed:", err)
      }
    },
  },
})

export function resetFeaturesStore(): void {
  useFeaturesStore().$reset()
}
