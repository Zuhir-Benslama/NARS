// ─── LABELS & LAYER VISIBILITY ───────────────────────────────────────────────

import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import maplibregl from "maplibre-gl"
import { getCtx } from "../core/state"
import { debugLog } from "../../utils/debug"

const PHASE_VISIBILITY: Record<string, string[]> = {
  areas: ["areas"],
  districts: ["areas", "districts"],
  cityCenter: ["areas", "cityCenter"],
  roads: ["areas", "cityCenter", "roads"],
  houseEntrances: ["areas", "cityCenter", "roads", "houseEntrances"],
  publicBuildings: ["areas", "publicBuildings"],
  publicSpaces: ["areas", "publicSpaces"],
  namingPanels: ["areas", "districts", "roads", "publicBuildings", "publicSpaces", "namingPanels"],
}

const KNOWN_PHASE_KEYS = PHASES.map((p) => p.key)

export function refreshAllEdgeLabels(): void {
  refreshLayerVisibility()
}

export function refreshLayerVisibility(): void {
  const appStore = useAppStore()
  const currentPhaseKey = PHASES[appStore.currentPhase]?.key

  const map = getCtx().map
  if (!map) return

  const visibleLayers = PHASE_VISIBILITY[currentPhaseKey] || [currentPhaseKey]
  const phaseFilter =
    visibleLayers.length === 1
      ? ["==", ["get", "phaseKey"], visibleLayers[0]]
      : ["any", ...visibleLayers.map((layer) => ["==", ["get", "phaseKey"], layer])]

  debugLog(
    "[LAYERS] currentPhase:",
    appStore.currentPhase,
    "key:",
    currentPhaseKey,
    "visible:",
    visibleLayers,
  )

  // Keep matrix-driven visibility from Phases.xlsx, but fail-safe render
  // legacy data when phaseKey is absent or not recognized.
  const phaseScopedFilter = [
    "any",
    phaseFilter,
    ["!", ["has", "phaseKey"]],
    ["!", ["in", ["get", "phaseKey"], ["literal", KNOWN_PHASE_KEYS]]],
  ]

  const layerGeometryTypes: Record<string, string> = {
    "nars-polygon-fill": "Polygon",
    "nars-polygon-stroke": "Polygon",
    "nars-polygon-label": "Polygon",
    "nars-line": "LineString",
    "nars-line-label": "LineString",
    "nars-point": "Point",
    "nars-point-label": "Point",
  }

  for (const [layerId, geomType] of Object.entries(layerGeometryTypes)) {
    if (map.getLayer(layerId)) {
      const combinedFilter = ["all", ["==", ["geometry-type"], geomType], phaseScopedFilter]
      map.setFilter(layerId, combinedFilter as maplibregl.FilterSpecification)
    }
  }

  // Endpoint layers are only visible during roads and houseEntrances phases
  const showEndpoints = ["roads", "houseEntrances"].includes(currentPhaseKey)
  const endpointLayers = [
    "nars-endpoint-start",
    "nars-endpoint-start-label",
    "nars-endpoint-end",
    "nars-endpoint-end-label",
  ]
  for (const layerId of endpointLayers) {
    if (map.getLayer(layerId)) {
      map.setLayoutProperty(layerId, "visibility", showEndpoints ? "visible" : "none")
    }
  }
}
