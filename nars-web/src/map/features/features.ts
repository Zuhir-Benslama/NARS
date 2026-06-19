// ─── FEATURE DATA, SAVE & MODAL HELPERS ──────────────────────────────────────

import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useModalStore } from "../../stores/modalStore"
import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import { apiFetch } from "../../api"
import { checkMainUrbanExists, getRoadSide } from "../../lib/validation"
import { debugLog, debugError } from "../../utils/debug"
import { logError, createServerError } from "../../lib/errors"
import type { FeatureData, LayerEntry, SaveResult, ModalResult } from "../../types"

// ─── GEOMETRY COORDINATE EXTRACTOR ────────────────────────────────────────────

function extractCoords(geometry: GeoJSON.Geometry): { lat: number; lng: number }[] | null {
  switch (geometry.type) {
    case "Point": {
      return [{ lat: geometry.coordinates[1], lng: geometry.coordinates[0] }]
    }
    case "LineString": {
      return geometry.coordinates.map((c) => ({ lat: c[1], lng: c[0] }))
    }
    case "Polygon": {
      return geometry.coordinates[0].map((c) => ({ lat: c[1], lng: c[0] }))
    }
    case "MultiPolygon": {
      const coords = geometry.coordinates[0][0].map((c) => ({ lat: c[1], lng: c[0] }))
      debugLog("[SAVE] MultiPolygon flattened to single Polygon (first ring)")
      return coords
    }
    default:
      return null
  }
}

// ─── FEATURE DATA BUILDER ────────────────────────────────────────────────────

export function buildFeatureData(
  geometry: GeoJSON.Geometry,
  phase: (typeof PHASES)[number],
  modalResult: ModalResult,
): FeatureData {
  const base: FeatureData = {
    type: phase.key,
    label: modalResult.label,
    decisionNumber: modalResult.decisionNumber,
    decisionDate: modalResult.decisionDate,
    areaTypeKey: modalResult.areaTypeKey,
    districtTypeKey: modalResult.districtTypeKey,
    roadTypeKey: modalResult.roadTypeKey,
    entranceTypeKey: modalResult.entranceTypeKey,
    roadDbId: modalResult.roadDbId,
    roadLabel: modalResult.roadLabel,
    side: modalResult.side,
    entranceNumber: modalResult.entranceNumber,
    mainEntranceDbId: modalResult.mainEntranceDbId,
    mainEntranceLabel: modalResult.mainEntranceLabel,
    bisNumber: modalResult.bisNumber,
    spaceTypeKey: modalResult.spaceTypeKey,
    sectorKey: modalResult.sectorKey,
    buildingTypeKey: modalResult.buildingTypeKey,
  }

  if (phase.key === "cityCenter") {
    base.radius = modalResult.radius as number | undefined
  }

  const coords = extractCoords(geometry)

  if (!coords) {
    debugError("[SAVE] Unknown geometry type:", geometry.type, geometry)
    return base
  }

  const result: FeatureData = {
    ...base,
    coordinates: coords,
    ...(geometry.type === "Point" ? { lat: coords[0].lat, lng: coords[0].lng } : {}),
  }

  if (geometry.type === "Point") {
    const pt = geometry as GeoJSON.Point & { radius?: number }
    if (pt.radius != null) {
      result.radius = pt.radius
    }
  }

  debugLog(
    "[SAVE] buildFeatureData — type:",
    result.type,
    "geometry:",
    result.lat != null ? `Point(${result.lat}, ${result.lng})` : `${coords.length} coords`,
    "keys:",
    Object.keys(result),
  )

  return result
}

// ─── API SHAPE MAPPING ────────────────────────────────────────────────────────

export function toApiSaveShape(fd: FeatureData): { type: string; layer: string } | null {
  switch (fd.type) {
    case "areas":
      return { type: "area", layer: fd.areaTypeKey ?? "central_urban" }
    case "cityCenter":
      return { type: "city_center", layer: "city_center" }
    case "districts":
      return { type: "district", layer: fd.districtTypeKey ?? "district" }
    case "roads":
      return { type: "road", layer: fd.roadTypeKey ?? "street" }
    case "houseEntrances":
      return {
        type: "house_entrance",
        layer: fd.entranceTypeKey ?? "main_entrance",
      }
    case "publicBuildings":
      return {
        type: "public_building",
        layer: fd.buildingTypeKey ?? "public_building",
      }
    case "publicSpaces":
      return { type: "public_space", layer: fd.spaceTypeKey ?? "garden" }
    case "namingPanels":
      return { type: "naming_panel", layer: "naming_panel" }
    default:
      return null
  }
}

// ─── DATABASE SAVE ────────────────────────────────────────────────────────────

export async function saveToDatabase(featureData: FeatureData): Promise<SaveResult> {
  try {
    const shape = toApiSaveShape(featureData)
    if (!shape) return { ok: false, error: `Unknown type '${featureData.type}'.` }

    const data = (await apiFetch("/api/save", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        type: shape.type,
        layer: shape.layer,
        label: featureData.label,
        data: featureData,
      }),
    }).then((r) => r.json())) as { id: string }
    return { ok: true, data }
  } catch (err) {
    const message = err instanceof Error ? err.message : "Unknown error"
    const cause =
      err instanceof Error && "cause" in err
        ? String((err as Error & { cause?: unknown }).cause)
        : undefined
    logError(createServerError(message, { action: "saveToDatabase" }, err))
    debugError("[SAVE] Database save failed:", {
      message,
      context: cause,
      stack: err instanceof Error ? err.stack : undefined,
    })
    return { ok: false, error: message }
  }
}

// ─── MODAL EXTRA PREPARATION ──────────────────────────────────────────────────

export async function prepareModalExtras(phase: (typeof PHASES)[number]): Promise<void> {
  const modalStore = useModalStore()
  const appStore = useAppStore()
  const layerStore = useLayerStore()
  const state = layerStore.$state as LayerState

  if (phase.key === "areas") {
    modalStore.mainUrbanExists = await checkMainUrbanExists()
    if (!modalStore.mainUrbanExists) {
      modalStore.label = appStore.communeName
    } else {
      modalStore.label = ""
    }
    modalStore.areaTypeKey = modalStore.mainUrbanExists ? "secondary_urban" : "central_urban"
  }

  if (phase.key === "houseEntrances") {
    modalStore.roadOptions = (state.roads || []).map((r, i) => ({
      idx: i,
      label: r.data.label || `Road ${i + 1}`,
      dbId: String(r.dbId),
    }))
    modalStore.mainEntranceOptions = (state.houseEntrances || [])
      .filter((e: LayerEntry) => e.data.entranceTypeKey === "main_entrance")
      .map((e, i) => ({
        idx: i,
        label: e.data.label || `Entrance ${i + 1}`,
        dbId: String(e.dbId),
      }))
  }
}

// ─── ROAD-SIDE & BIS HELPERS ──────────────────────────────────────────────────

export async function fetchRoadSide(roadDbId: string): Promise<void> {
  const modalStore = useModalStore()
  modalStore.entranceSideLoading = true
  modalStore.entranceSide = null
  modalStore.entranceNumber = null

  // Try to get position from current drawing geometry (dev-only global)
  const narsWindow = window as Window & {
    __narsCurrentGeometry?: [number, number][] | null
  }
  const currentGeometry = import.meta.env.DEV ? narsWindow.__narsCurrentGeometry : undefined
  let lat: number | undefined
  let lng: number | undefined

  if (currentGeometry && currentGeometry.length > 0) {
    // Use the last vertex position (the one being placed)
    ;[lng, lat] = currentGeometry[currentGeometry.length - 1]
  }

  if (lat && lng) {
    const result = await getRoadSide(roadDbId, lat, lng)
    if (result) {
      modalStore.entranceSide = result.side
      modalStore.entranceNumber = result.suggestedNumber
    }
  }

  modalStore.entranceSideLoading = false
}

export function computeBisNumber(mainEntranceDbId: string): void {
  const layerStore = useLayerStore()
  const st = layerStore.$state as LayerState
  const count = (st.houseEntrances || []).filter(
    (e: LayerEntry) =>
      e.data.entranceTypeKey === "secondary_entrance" &&
      e.data.mainEntranceDbId === mainEntranceDbId,
  ).length
  const modalStore = useModalStore()
  modalStore.bisNumber = count + 1
  modalStore.label = "BIS" + String(count + 1).padStart(2, "0")
}
