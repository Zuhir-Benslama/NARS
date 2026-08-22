// ─── LOADER DB ────────────────────────────────────────────────────────────────
// loadFromDatabase: fetches features from API, maps to layers, and populates
// the feature store with a single setData call for performance.

import { apiFetch } from "../../api"
import { PHASES, getApiLayerToPhase } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore, LAYER_KEYS } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import { type MaplibreFeature } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"
import { addScatteredArea, clearScatteredAreas } from "../rendering/geometry"
import { refreshLayerVisibility } from "../rendering/labels"
import { getFeatureType } from "../house-numbering"
import { debugError, debugLog } from "../../utils/debug"
import { updateEndpointMarkers } from "../roads/road-directions"
import { loadPhase } from "../../phases-nav/storage"
import type {
  FeatureData,
  FeatureDataByType,
  FeatureTypeKey,
  LayerEntry,
  DbFeature,
} from "../../types"
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

function resolvePhaseKey(feature: DbFeature): FeatureTypeKey | undefined {
  const apiLayerToPhase = getApiLayerToPhase()
  let key = apiLayerToPhase[feature.layer]
  if (!key && feature.feature_type) key = apiLayerToPhase[feature.feature_type]
  if (!key) {
    const inferred = TYPE_TO_LAYER[feature.feature_type || ""]
    if (inferred) key = getApiLayerToPhase()[inferred]
  }
  return key
}

function processFeature(
  feature: DbFeature,
  layerStore: ReturnType<typeof useLayerStore>,
  maplibreFeatures: MaplibreFeature[],
): "ok" | "scattered" {
  try {
    const rawData: FeatureData =
      typeof feature.data === "string" ? JSON.parse(feature.data) : feature.data

    if (feature.layer === "scattered") {
      if (rawData.geometry) addScatteredArea(rawData.geometry)
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
        rawData?.type,
      )
      return "ok"
    }

    const data = rawData as FeatureDataByType

    const layerEntry: LayerEntry = {
      id: `feat_${feature.id}`,
      dbId: feature.id,
      data,
      type: getFeatureType(phase.drawType),
    }
    layerStore.addFeature(phaseKey as keyof LayerState, layerEntry)

    const geojsonFeature = buildGeoJsonFeature(feature.id, data, phase)
    if (geojsonFeature) {
      maplibreFeatures.push({
        id: `feat_${feature.id}`,
        geometry: geojsonFeature.geometry,
        properties: geojsonFeature.properties as MaplibreFeature["properties"],
      })
    }
  } catch (err) {
    debugError("[LOAD] Error loading feature", feature.id, ":", err)
  }
  return "ok"
}

export async function loadFromDatabase(): Promise<void> {
  const featuresStore = useFeaturesStore()
  const appStore = useAppStore()
  debugLog("[LOAD] Starting...")
  appStore.setLoading(true)
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
    for (const key of LAYER_KEYS) {
      layerStore.clearLayer(key)
    }
    featuresStore.clear()
    // Reset hit-testing state once for the whole load; each scattered feature
    // below APPENDS via processFeature. (The previous per-feature reset made
    // multiple scattered features clobber each other — only the last survived.)
    clearScatteredAreas()

    const maplibreFeatures: MaplibreFeature[] = []

    for (const feature of features) {
      const result = processFeature(feature, layerStore, maplibreFeatures)
      if (result === "scattered") continue
    }

    featuresStore.batchAdd(maplibreFeatures)
    debugLog("[LOAD] batchAdd", maplibreFeatures.length, "features into features source")

    const communeId = appStore.user?.commune?.id ?? null
    const persistedPhase = loadPhase(communeId)

    if (
      typeof persistedPhase === "number" &&
      persistedPhase >= 0 &&
      persistedPhase < PHASES.length
    ) {
      appStore.setCurrentPhase(persistedPhase)
    } else {
      appStore.setCurrentPhase(0)
    }

    refreshLayerVisibility()
    updateEndpointMarkers()
    debugLog("[LOAD] Loading complete")
  } catch (err) {
    debugError("Load error:", err)
    appStore.setLoadError(true)
  } finally {
    appStore.setLoading(false)
  }
}
