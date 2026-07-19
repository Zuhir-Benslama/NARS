// ─── DRAW SAVE ────────────────────────────────────────────────────────────────
// Handles saving a completed drawing to the database, updating the feature store,
// and resetting the draw mode for the next feature.

import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useLayerStore } from "../../stores/layerStore"
import type { FeatureData } from "../../types"
import { getCtx } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"

const CITY_CENTER_MIN_RADIUS_M = 5
const CITY_CENTER_MAX_RADIUS_M = 50_000
import { buildDrawControl } from "./draw-control"
import { refreshLayerVisibility } from "../rendering/labels"
import { computeCircleRing, computeCircleRadius, closeRing } from "../rendering/geometry"
import { getFeatureType } from "../house-numbering"
import { getErrorMessage } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import { debugError } from "../../utils/debug"
import type { ModalResult } from "../../types"
import { updateEndpointMarkers } from "../roads/road-directions"
import { buildFeatureData } from "../features/feature-data"
import { DRAW_CONFIG } from "../../config"
import { saveToDatabase } from "../features/feature-persistence"
import { openModalForFeature } from "./draw-modal"
import { getDrawingPhase, setSavingFeature, repatchMarker } from "./draw-state"
import { areaStyle } from "../rendering/styles"
import { delay } from "../../utils/time"

// ─── GEOMAN CLEANUP HELPER ─────────────────────────────────────────────────────
// Geoman's feature data type is poorly typed — accessing .delete() requires
// an unchecked cast. Extracting it here avoids repeating the cast pattern.

interface GeomanFeature {
  delete?: () => void
}

function deleteGeomanFeature(feature: Record<string, unknown>): void {
  try {
    ;(feature as unknown as GeomanFeature).delete?.()
  } catch (err) {
    debugError("[GEOMAN] delete:", err)
  }
}

// ─── GEOMETRY NORMALIZATION ───────────────────────────────────────────────────

export function normalizeGeometry(
  geometry: GeoJSON.Geometry,
  drawType: string,
): GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon {
  if (drawType === "polyline" && geometry.type === "Polygon") {
    const ring = geometry.coordinates[0].map((c) => [c[0], c[1]] as [number, number])
    return { type: "LineString", coordinates: ring }
  }
  if (drawType === "polygon" && geometry.type === "LineString") {
    const ring = geometry.coordinates.map((c) => [c[0], c[1]] as [number, number])
    return { type: "Polygon", coordinates: [closeRing(ring)] }
  }
  return geometry as GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon
}

// ─── FEATURE STYLE ────────────────────────────────────────────────────────────

export function getFeatureStyle(
  phase: (typeof PHASES)[number],
  modalResult: ModalResult,
): Record<string, unknown> {
  const style: Record<string, unknown> = {
    fillColor: phase.color,
    fillOpacity: 0.1,
    lineColor: phase.color,
    lineWidth: 2,
    circleColor: phase.color,
    circleRadius: 8,
    textColor: "#333333",
  }

  if (phase.key === "areas") {
    const s = areaStyle(modalResult.areaTypeKey ?? "central_urban")
    style.fillColor = s.lineColor
    style.fillOpacity = 0
    style.lineColor = s.lineColor
    style.lineWidth = s.lineWidth
  } else if (phase.key === "districts") {
    style.fillColor = phase.color
    style.fillOpacity = 0
    style.lineColor = phase.color
    style.lineWidth = 3
  } else if (phase.key === "publicBuildings") {
    style.fillColor = phase.color
    style.fillOpacity = 0.25
    style.lineColor = phase.color
    style.lineWidth = 3
  } else if (phase.key === "publicSpaces") {
    style.fillColor = phase.color
    style.fillOpacity = 0.2
    style.lineColor = phase.color
    style.lineWidth = 3
  } else if (phase.drawType === "polyline") {
    style.lineColor = phase.color
    style.lineWidth = 8
    delete style.fillColor
    delete style.fillOpacity
  } else if (phase.key === "houseEntrances") {
    style.circleColor = phase.color
    style.circleRadius = 10
    style.textColor = "#000000"
  }

  return style
}

// ─── CITY CENTER CHECK ───────────────────────────────────────────

async function checkExistingCityCenter(
  geomanFeatureData: Record<string, unknown>,
): Promise<boolean> {
  if (getDrawingPhase()?.key !== "cityCenter") return true
  const layerStore = useLayerStore()
  const state = layerStore.$state
  if ((state.cityCenter?.length ?? 0) === 0) return true

  showToast("A city center already exists. Delete it first to create a new one.", "error")
  deleteGeomanFeature(geomanFeatureData)
  return false
}

// ─── CITY CENTER GEOMETRY OVERRIDE ─────────────────────────────────

function applyCityCenterOverride(
  featureData: FeatureData,
  style: Record<string, unknown>,
  storeGeometry: GeoJSON.Geometry,
): { style: Record<string, unknown>; storeGeometry: GeoJSON.Geometry } {
  if (!featureData.radius) return { style, storeGeometry }
  const ring = closeRing(computeCircleRing(featureData.lat!, featureData.lng!, featureData.radius))
  return {
    storeGeometry: { type: "LineString", coordinates: ring },
    style: {
      lineColor: "#e74c3c",
      lineWidth: 6,
      textColor: "#333333",
      radius: featureData.radius,
    },
  }
}

// ─── STORE PAYLOAD BUILDER ──────────────────────────────────────────

interface StorePayload {
  dbId: string
  style: Record<string, unknown>
  storeGeometry: GeoJSON.Geometry
}

function buildStorePayload(
  saveId: string,
  geometry: GeoJSON.Geometry,
  drawingPhase: (typeof PHASES)[number],
  modalResult: ModalResult,
  featureData: FeatureData,
): StorePayload {
  const style = getFeatureStyle(drawingPhase, modalResult)
  const storeGeometry = normalizeGeometry(geometry, drawingPhase.drawType)

  if (drawingPhase.key !== "cityCenter") return { dbId: saveId, style, storeGeometry }

  const overridden = applyCityCenterOverride(featureData, style, storeGeometry)
  return { dbId: saveId, style: overridden.style, storeGeometry: overridden.storeGeometry }
}

// ─── STORE & UI UPDATE ──────────────────────────────────────────────

async function updateStoresAfterSave(
  featureId: string,
  payload: StorePayload,
  drawingPhase: (typeof PHASES)[number],
  featureData: FeatureData,
  narsDrawType: string,
): Promise<void> {
  const featuresStore = useFeaturesStore()
  featuresStore.add({
    id: featureId,
    geometry: payload.storeGeometry,
    properties: {
      dbId: payload.dbId,
      phaseKey: drawingPhase.key,
      label: featureData.label,
      geomType: payload.storeGeometry.type,
      ...payload.style,
    },
  })

  const layerStore = useLayerStore()
  const phaseKey = drawingPhase.key
  layerStore.addFeature(phaseKey, {
    id: featureId,
    dbId: payload.dbId,
    data: featureData,
    type: getFeatureType(narsDrawType),
  })

  useAppStore().syncCounts()
  refreshLayerVisibility()
  if (drawingPhase.key === "roads") updateEndpointMarkers()
  showToast("Feature saved.", "success")
}

// ─── SAVE & UPDATE STORE ──────────────────────────────────────────

async function saveAndUpdateStore(
  geometry: GeoJSON.Geometry,
  drawingPhase: (typeof PHASES)[number],
  modalResult: ModalResult,
  narsDrawType: string,
  featureId: string,
  geomanFeatureData: Record<string, unknown>,
): Promise<void> {
  setSavingFeature(true)
  try {
    const featureData = buildFeatureData(geometry, drawingPhase, modalResult)
    const saveResult = await saveToDatabase(featureData)
    if (!saveResult.ok || !saveResult.data) {
      showToast("Save failed: " + (saveResult.error ?? "Please try again."), "error")
      return
    }

    const payload = buildStorePayload(
      saveResult.data.id,
      geometry,
      drawingPhase,
      modalResult,
      featureData,
    )
    await updateStoresAfterSave(featureId, payload, drawingPhase, featureData, narsDrawType)

    await delay(DRAW_CONFIG.geomanCleanupDelayMs)
    deleteGeomanFeature(geomanFeatureData)
  } catch (err) {
    debugError("[COMPLETE] Save error:", err)
    showToast("Save failed: " + getErrorMessage(err), "error")
  } finally {
    setSavingFeature(false)
    await delay(DRAW_CONFIG.drawModeResetDelayMs)
    await resetDrawMode()
  }
}

// ─── COMPLETE DRAWING ─────────────────────────────────────────────────────────

export async function completeDrawingWithGeometry(
  geometry: GeoJSON.Geometry,
  narsDrawType: string,
  geomanFeatureData: Record<string, unknown>,
): Promise<void> {
  const drawingPhase = getDrawingPhase()
  if (!drawingPhase) return

  if (!(await checkExistingCityCenter(geomanFeatureData))) return
  if (!validateGeometry(geometry, drawingPhase, geomanFeatureData)) return

  const featureId = crypto.randomUUID()
  const gm = getCtx().geoman
  if (gm) {
    try {
      await gm.disableDraw()
    } catch (err) {
      debugError("[DRAW-SAVE] disableDraw:", err)
    }
  }

  const modalResult = await openModalForFeature(drawingPhase, featureId, geometry)
  if (!modalResult) {
    deleteGeomanFeature(geomanFeatureData)
    buildDrawControl({
      key: drawingPhase.key,
      drawType: drawingPhase.drawType,
      color: drawingPhase.color,
    })
    repatchMarker()
    return
  }

  await saveAndUpdateStore(
    geometry,
    drawingPhase,
    modalResult,
    narsDrawType,
    featureId,
    geomanFeatureData,
  )
}

// ─── GEOMETRY VALIDATION ──────────────────────────────────────────────────────

function validateGeometry(
  geometry: GeoJSON.Geometry,
  phase: (typeof PHASES)[number],
  geomanFeatureData: Record<string, unknown>,
): boolean {
  const cleanup = () => deleteGeomanFeature(geomanFeatureData)

  if (geometry.type === "LineString" && geometry.coordinates.length < 2) {
    showToast("Road must have at least 2 points.", "error")
    cleanup()
    return false
  }
  if (
    geometry.type === "Polygon" &&
    (!geometry.coordinates[0] || geometry.coordinates[0].length < 3)
  ) {
    showToast("Area must have at least 3 points.", "error")
    cleanup()
    return false
  }
  if (phase.key === "cityCenter") {
    let radius = (geometry as { radius?: number }).radius
    if (geometry.type === "Polygon" && geometry.coordinates[0]?.length >= 3) {
      const coords = geometry.coordinates[0] as [number, number][]
      let sumLat = 0,
        sumLng = 0
      for (const [lng, lat] of coords) {
        sumLat += lat
        sumLng += lng
      }
      const centerLat = sumLat / coords.length
      const centerLng = sumLng / coords.length
      radius = computeCircleRadius(centerLat, centerLng, coords)
    }
    if (!radius || Number.isNaN(radius) || radius < CITY_CENTER_MIN_RADIUS_M) {
      showToast("City center radius is too small (minimum 5 meters).", "error")
      cleanup()
      return false
    }
    if (radius > CITY_CENTER_MAX_RADIUS_M) {
      showToast("City center radius is too large (maximum 50 km).", "error")
      cleanup()
      return false
    }
  }
  return true
}

// ─── DRAW MODE RESET ──────────────────────────────────────────────────────────

async function resetDrawMode(): Promise<void> {
  const gm = getCtx().geoman
  const phase = getDrawingPhase()
  if (!gm || !phase) return

  try {
    await gm.disableDraw()
  } catch (err) {
    debugError("[DRAW-SAVE] reset disableDraw:", err)
  }
  buildDrawControl({
    key: phase.key,
    drawType: phase.drawType,
    color: phase.color,
  })
  repatchMarker()
}
