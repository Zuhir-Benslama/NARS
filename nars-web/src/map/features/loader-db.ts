// ─── LOADER DB ────────────────────────────────────────────────────────────────
// loadFromDatabase: fetches features from API, maps to layers, and populates
// the feature store with a single setData call for performance.

import { apiFetch } from "../../api"
import { PHASES, API_LAYER_TO_PHASE } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import { featuresStore, type MaplibreFeature } from "../core/state"
import { renderScatteredAreas } from "../rendering/geometry"
import { refreshLayerVisibility } from "../rendering/labels"
import { getFeatureType } from "../house-numbering"
import { debugError, debugLog } from "../../utils/debug"
import { updateEndpointMarkers } from "../roads/road-directions"
import { loadPhase } from "../phases/phase-storage"
import type { FeatureData, LayerEntry, DbFeature } from "../../types"
import { buildGeoJsonFeature } from "./loader-build"

// ─── PER-FEATURE PROCESSING ──────────────────────────────────────

const TYPE_TO_LAYER: Record<string, string> = {
  area: "central_urban",
  road: "street",
  district: "district",
  house_entrance: "main_entrance",
  public_building: "public_building",
  public_space: "garden",
  city_center: "city_center",
  naming_panel: "naming_panel",
}

function resolvePhaseKey(feature: DbFeature): string | undefined {
  let key = API_LAYER_TO_PHASE[feature.layer]
  if (!key && feature.feature_type) key = API_LAYER_TO_PHASE[feature.feature_type]
  if (!key) {
    const inferred = TYPE_TO_LAYER[feature.feature_type || ""]
    if (inferred) key = API_LAYER_TO_PHASE[inferred]
  }
  return key
}

function processFeature(
  feature: DbFeature,
  state: LayerState,
  maplibreFeatures: MaplibreFeature[],
): "ok" | "scattered" {
  try {
    const data: FeatureData =
      typeof feature.data === "string" ? JSON.parse(feature.data) : feature.data

    if (feature.layer === "scattered") {
      if (data.geometry) renderScatteredAreas(data.geometry as string)
      return "scattered"
    }

    const phaseKey = resolvePhaseKey(feature)
    const phase = PHASES.find((p) => p.key === phaseKey)
    if (!phaseKey || !phase) {
      debugError(
        "[LOAD] Skipped feature",
        feature.id,
        "- unknown layer/type:",
        feature.layer,
        data?.type,
      )
      return "ok"
    }

    const layerEntry: LayerEntry = {
      id: `feat_${feature.id}`,
      dbId: feature.id,
      data,
      type: getFeatureType(phase.drawType),
    }
    ;(state[phaseKey as keyof LayerState] as LayerEntry[]).push(layerEntry)

    const geojsonFeature = buildGeoJsonFeature(feature.id, data, phase)
    if (geojsonFeature) {
      maplibreFeatures.push({
        id: `feat_${feature.id}`,
        geometry: geojsonFeature.geometry,
        properties: geojsonFeature.properties as MaplibreFeature["properties"],
      })
    }

    if (phase.key === "cityCenter" && data.lat != null && data.lng != null) {
      const appStore = useAppStore()
      appStore.cityCenterMode = "city_center"
      appStore.cityCenterLatLng = { lat: data.lat, lng: data.lng }
    }
  } catch (err) {
    debugError("[LOAD] Error loading feature:", err)
  }
  return "ok"
}

export async function loadFromDatabase(): Promise<void> {
  const appStore = useAppStore()
  debugLog("[LOAD] Starting...")
  appStore.isLoading = true
  try {
    debugLog("[LOAD] Fetching /api/features...")
    const response = await apiFetch("/api/features")
    debugLog("[LOAD] Response status:", response.status)
    const json = (await response.json()) as { features?: DbFeature[]; count?: number } | DbFeature[]

    const features: DbFeature[] = Array.isArray(json) ? json : (json.features ?? [])
    debugLog("[LOAD] API returned", features.length, "features")

    if (!features.length) {
      debugLog("[LOAD] No saved features in database.")
      return
    }

    const layerStore = useLayerStore()
    const state = layerStore.$state as LayerState
    const phaseKeys = Object.keys(state) as (keyof LayerState)[]

    for (const key of phaseKeys) {
      state[key] = []
    }
    featuresStore.clear()

    const maplibreFeatures: MaplibreFeature[] = []

    for (const feature of features) {
      const result = processFeature(feature, state, maplibreFeatures)
      if (result === "scattered") continue
    }

    featuresStore.batchAdd(maplibreFeatures)
    debugLog("[LOAD] batchAdd", maplibreFeatures.length, "features into features source")

    const communeId =
      (appStore.user as { commune?: { id?: number | string } } | null | undefined)?.commune?.id ??
      null
    const persistedPhase = loadPhase(communeId)

    if (
      typeof persistedPhase === "number" &&
      persistedPhase >= 0 &&
      persistedPhase < PHASES.length
    ) {
      appStore.currentPhase = persistedPhase
    } else {
      appStore.currentPhase = 0
    }

    appStore.syncCounts()
    refreshLayerVisibility()
    updateEndpointMarkers()
    debugLog("[LOAD] Loading complete")
  } catch (err) {
    debugError("Load error:", err)
    appStore.loadError = true
  } finally {
    appStore.isLoading = false
  }
}
