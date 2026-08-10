// ─── HOUSE ENTRANCE REFERENCE HELPERS ────────────────────────────────────────
// Manages the reference road / reference entrance selection used when placing
// house entrance markers, and implements the Set House Numbers algorithm.
// Extracted from context-menu.ts for size.

import { useAppStore } from "../stores/appStore"
import { useLayerStore } from "../stores/layerStore"
import type { LayerState } from "../stores/layerStore"
import { useFeaturesStore } from "../stores/featuresStore"

const REFERENCE_COLOR = "#f39c12"
const DEFAULT_ROAD_COLOR = "#3498db"
const DEFAULT_ENTRANCE_COLOR = "#27ae60"

function parseGeometry(raw: string | undefined): GeoJSON.Geometry | undefined {
  if (!raw) return undefined
  try {
    return JSON.parse(raw)
  } catch {
    return undefined
  }
}

function highlightFeature(phaseKey: string, dbId: string, active: boolean): void {
  const featuresStore = useFeaturesStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state
  const entries = state[phaseKey as keyof LayerState] || []
  const entry = entries.find((e) => e.dbId === dbId)
  if (!entry) return

  const geometry = parseGeometry(entry.data.geometry)

  if (phaseKey === "roads") {
    featuresStore.update(entry.id, {
      geometry,
      properties: {
        ...entry.data,
        phaseKey,
        lineColor: active ? REFERENCE_COLOR : DEFAULT_ROAD_COLOR,
      },
    })
  } else if (phaseKey === "houseEntrances") {
    featuresStore.update(entry.id, {
      geometry,
      properties: {
        ...entry.data,
        phaseKey,
        circleColor: active ? REFERENCE_COLOR : DEFAULT_ENTRANCE_COLOR,
      },
    })
  }
}

export function setReferenceRoad(dbId: string): void {
  const appStore = useAppStore()
  if (appStore.referenceRoadDbId != null) {
    highlightFeature("roads", appStore.referenceRoadDbId, false)
  }
  appStore.setReferenceRoad(dbId)
  highlightFeature("roads", dbId, true)
}

export function clearReferenceRoad(): void {
  const appStore = useAppStore()
  if (appStore.referenceRoadDbId != null) {
    highlightFeature("roads", appStore.referenceRoadDbId, false)
    appStore.setReferenceRoad(null)
  }
}

export function setReferenceEntrance(dbId: string): void {
  const appStore = useAppStore()
  if (appStore.referenceEntranceDbId != null) {
    highlightFeature("houseEntrances", appStore.referenceEntranceDbId, false)
  }
  appStore.setReferenceEntrance(dbId)
  highlightFeature("houseEntrances", dbId, true)
}

export { setHouseNumbers } from "./house-numbering"
