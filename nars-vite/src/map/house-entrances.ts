// ─── HOUSE ENTRANCE REFERENCE HELPERS ────────────────────────────────────────
// Manages the reference road / reference entrance selection used when placing
// house entrance markers, and implements the Set House Numbers algorithm.
// Extracted from context-menu.ts for size.

import { store } from "../store"
import { useLayerStore } from "../stores/layerStore"
import type { LayerState } from "../stores/layerStore"
import { featuresStore } from "./core/state"

const REFERENCE_COLOR = "#f39c12"
const DEFAULT_ROAD_COLOR = "#3498db"
const DEFAULT_ENTRANCE_COLOR = "#27ae60"

function highlightFeature(phaseKey: string, dbId: string, active: boolean): void {
  const layerStore = useLayerStore()
  const state = layerStore.$state as LayerState
  const entries = state[phaseKey as keyof LayerState] || []
  const entry = entries.find((e) => e.dbId === dbId)
  if (!entry) return

  if (phaseKey === "roads") {
    featuresStore.update(entry.id, {
      geometry: entry.data.geometry ? JSON.parse(entry.data.geometry) : undefined,
      properties: {
        ...entry.data,
        phaseKey,
        lineColor: active ? REFERENCE_COLOR : DEFAULT_ROAD_COLOR,
      },
    })
  } else if (phaseKey === "houseEntrances") {
    featuresStore.update(entry.id, {
      geometry: entry.data.geometry ? JSON.parse(entry.data.geometry) : undefined,
      properties: {
        ...entry.data,
        phaseKey,
        circleColor: active ? REFERENCE_COLOR : DEFAULT_ENTRANCE_COLOR,
      },
    })
  }
}

export function setReferenceRoad(dbId: string): void {
  if (store.referenceRoadDbId != null) {
    highlightFeature("roads", store.referenceRoadDbId, false)
  }
  store.referenceRoadDbId = dbId
  highlightFeature("roads", dbId, true)
}

export function clearReferenceRoad(): void {
  if (store.referenceRoadDbId != null) {
    highlightFeature("roads", store.referenceRoadDbId, false)
    store.referenceRoadDbId = null
  }
}

export function setReferenceEntrance(dbId: string): void {
  if (store.referenceEntranceDbId != null) {
    highlightFeature("houseEntrances", store.referenceEntranceDbId, false)
  }
  store.referenceEntranceDbId = dbId
  highlightFeature("houseEntrances", dbId, true)
}

export { setHouseNumbers } from "./house-numbering"
