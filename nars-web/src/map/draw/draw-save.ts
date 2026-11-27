// ─── DRAW SAVE ────────────────────────────────────────────────────────────────
// Handles saving a completed drawing to the database, updating the feature store,
// and resetting the draw mode for the next feature.

import { PHASES } from "../../phases"
import { useLayerStore } from "../../stores/layerStore"
import type { FeatureDataByType, LatLng, ModalResult } from "../../types"
import { getCtx } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"

import { buildDrawControl, clearEdgeVisibilityPoll } from "./draw-control"
import { refreshLayerVisibility } from "../rendering/labels"
import { computeCircleRadius, closeRing } from "../rendering/geometry"
import { CITY_CENTER_COLOR } from "../../phases"
import { getFeatureType } from "../house-numbering"
import { getUserMessageKey } from "../../lib/errors"
import { showToast } from "../../lib/toast"
import { debugError } from "../../utils/debug"
import { t } from "../../i18n"
import { updateEndpointMarkers } from "../roads/road-directions"
import { buildFeatureData, featureDataToGeometry } from "../features/feature-data"
import { displayLabelFor } from "../features/feature-labels"
import { DRAW_CONFIG, CITY_CENTER_CONFIG } from "../../config"
import { cityCenterRadiusError, plainCityCenterPlacementError } from "../../lib/city-center"
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
  if (geometry.type === "MultiPolygon" || geometry.type === "MultiLineString") {
    debugError("[NORMALIZE] Unexpected geometry type:", geometry.type)
    if (geometry.type === "MultiLineString") {
      return { type: "LineString", coordinates: geometry.coordinates[0] ?? [] }
    }
    const outer = (geometry as GeoJSON.MultiPolygon).coordinates[0]?.[0] ?? []
    return { type: "Polygon", coordinates: [outer.map((c) => [c[0], c[1]] as [number, number])] }
  }
  return geometry as GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon
}

// ─── FEATURE STYLE ────────────────────────────────────────────────────────────

export function getDefaultStyle(color: string): Record<string, unknown> {
  return {
    fillColor: color,
    fillOpacity: 0.1,
    lineColor: color,
    lineWidth: 2,
    circleColor: color,
    circleRadius: 8,
    textColor: "#333333",
  }
}

export function getFeatureStyle(
  phase: (typeof PHASES)[number],
  modalResult: ModalResult,
): Record<string, unknown> {
  const style = getDefaultStyle(phase.color)

  if (phase.key === "areas") {
    const areaType = modalResult.type === "areas" ? modalResult.areaTypeKey : undefined
    const s = areaStyle(areaType ?? "central_urban")
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

function cityCenterPoint(geometry: GeoJSON.Geometry): LatLng | null {
  if (geometry.type === "Point") {
    const [lng, lat] = geometry.coordinates as [number, number]
    return Number.isFinite(lng) && Number.isFinite(lat) ? { lat, lng } : null
  }
  if (geometry.type === "Polygon") {
    const ring = (geometry.coordinates as [number, number][][])[0] ?? []
    if (ring.length < 3) return null
    // Closing vertex is omitted so it does not skew the centroid.
    const vertices =
      ring.length > 1 &&
      ring[0][0] === ring[ring.length - 1][0] &&
      ring[0][1] === ring[ring.length - 1][1]
        ? ring.slice(0, -1)
        : ring
    const lat = vertices.reduce((sum, c) => sum + c[1], 0) / vertices.length
    const lng = vertices.reduce((sum, c) => sum + c[0], 0) / vertices.length
    return Number.isFinite(lat) && Number.isFinite(lng) ? { lat, lng } : null
  }
  return null
}

function checkCityCenterPlacement(geometry: GeoJSON.Geometry, isRoundabout: boolean): boolean {
  if (getDrawingPhase()?.key !== "cityCenter") return true
  // Roundabouts share the city-center table but are not capped per urban area.
  if (isRoundabout) return true

  const state = useLayerStore().$state
  const error = plainCityCenterPlacementError(
    cityCenterPoint(geometry),
    state.areas ?? [],
    state.cityCenter ?? [],
  )
  if (error) {
    showToast(
      t(
        error === "outside_urban_area"
          ? "alert_cc_must_be_in_urban_area"
          : "map_city_center_exists",
      ),
      "error",
    )
    return false
  }
  return true
}

// ─── CITY CENTER GEOMETRY OVERRIDE ─────────────────────────────────

function applyCityCenterOverride(
  featureData: FeatureDataByType,
  style: Record<string, unknown>,
  storeGeometry: GeoJSON.Geometry,
): { style: Record<string, unknown>; storeGeometry: GeoJSON.Geometry } {
  const d = featureData as { radius?: number; lat?: number; lng?: number }
  if (!d.radius || d.lat == null || d.lng == null) return { style, storeGeometry }
  return {
    storeGeometry: featureDataToGeometry(d, "circle"),
    style: {
      lineColor: CITY_CENTER_COLOR,
      lineWidth: CITY_CENTER_CONFIG.ringStrokeWidth,
      textColor: "#333333",
      radius: d.radius,
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
  featureData: FeatureDataByType,
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
  featureData: FeatureDataByType,
  narsDrawType: string,
): Promise<void> {
  const featuresStore = useFeaturesStore()
  featuresStore.add({
    id: featureId,
    geometry: payload.storeGeometry,
    properties: {
      dbId: payload.dbId,
      phaseKey: drawingPhase.key,
      label: displayLabelFor(drawingPhase, featureData),
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

  refreshLayerVisibility()
  if (drawingPhase.key === "roads") updateEndpointMarkers()
  // House entrances are cached locally and only persisted later (with their
  // computed numbers), so do not claim they were saved to the database now.
  if (drawingPhase.key !== "houseEntrances") {
    showToast(t("map_feature_saved"), "success")
  }
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
  let saveOk = false
  try {
    const featureData = buildFeatureData(geometry, drawingPhase, modalResult)

    // House entrances are cached locally and only persisted once their house
    // numbers have been computed (see house-numbering.ts), so drawing one
    // does not hit the database.
    if (drawingPhase.key === "houseEntrances") {
      const tempId = `local_${crypto.randomUUID()}`
      const payload = buildStorePayload(tempId, geometry, drawingPhase, modalResult, featureData)
      await updateStoresAfterSave(featureId, payload, drawingPhase, featureData, narsDrawType)
      saveOk = true

      await delay(DRAW_CONFIG.geomanCleanupDelayMs)
      deleteGeomanFeature(geomanFeatureData)
      return
    }

    const saveResult = await saveToDatabase(featureData)
    if (!saveResult.ok || !saveResult.data) {
      showToast(
        t("map_save_failed", { error: saveResult.error ?? t("map_save_failed_fallback") }),
        "error",
      )
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
    saveOk = true

    await delay(DRAW_CONFIG.geomanCleanupDelayMs)
    deleteGeomanFeature(geomanFeatureData)
  } catch (err) {
    debugError("[COMPLETE] Save error:", err)
    showToast(t("map_save_failed", { error: t(getUserMessageKey(err)) }), "error")
  } finally {
    setSavingFeature(false)
    if (saveOk) {
      await delay(DRAW_CONFIG.drawModeResetDelayMs)
      await resetDrawMode()
    }
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

  if (!validateGeometry(geometry, drawingPhase, geomanFeatureData)) return

  const featureId = crypto.randomUUID()
  const gm = getCtx().geoman
  if (gm) {
    clearEdgeVisibilityPoll()
    try {
      await gm.disableDraw()
    } catch (err) {
      debugError("[DRAW-SAVE] disableDraw:", err)
    }
  }

  const modalResult = await openModalForFeature(drawingPhase, featureId, geometry)
  const isRoundabout =
    modalResult?.type === "cityCenter" && modalResult.cityCenterTypeKey === "roundabout"
  if (modalResult && !checkCityCenterPlacement(geometry, isRoundabout)) {
    deleteGeomanFeature(geomanFeatureData)
    return
  }
  if (!modalResult) {
    deleteGeomanFeature(geomanFeatureData)
    void buildDrawControl({
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

// ─── TURN ANGLE HELPER ────────────────────────────────────────────────────────

function computeTurnAngle(
  lat1: number,
  lng1: number,
  lat2: number,
  lng2: number,
  lat3: number,
  lng3: number,
): number {
  const v1x = lng2 - lng1
  const v1y = lat2 - lat1
  const v2x = lng3 - lng2
  const v2y = lat3 - lat2
  const len1 = Math.hypot(v1x, v1y)
  const len2 = Math.hypot(v2x, v2y)
  if (len1 < 1e-10 || len2 < 1e-10) return 0
  const dot = (v1x * v2x + v1y * v2y) / (len1 * len2)
  return (Math.acos(Math.min(1, Math.max(-1, dot))) * 180) / Math.PI
}

// ─── GEOMETRY VALIDATION ──────────────────────────────────────────────────────

function isNullIslandPoint(geometry: GeoJSON.Geometry): boolean {
  if (geometry.type !== "Point") return false
  const [lng, lat] = geometry.coordinates
  return Number.isFinite(lng) && Number.isFinite(lat) && lng === 0 && lat === 0
}

function validateGeometry(
  geometry: GeoJSON.Geometry,
  phase: (typeof PHASES)[number],
  geomanFeatureData: Record<string, unknown>,
): boolean {
  const cleanup = () => deleteGeomanFeature(geomanFeatureData)

  // A degenerate marker at (0,0) is a placeholder, not a map position. It used
  // to be persisted as an entrance and hold the even/odd slot that belongs to
  // the first real entrance on that side.
  if (phase.key === "houseEntrances" && isNullIslandPoint(geometry)) {
    showToast(t("alert_entrance_invalid_position"), "error")
    cleanup()
    return false
  }

  if (geometry.type === "LineString" && geometry.coordinates.length < 2) {
    showToast(t("map_road_min_points"), "error")
    cleanup()
    return false
  }
  if (phase.key === "roads" && geometry.type === "LineString") {
    const coords = geometry.coordinates as [number, number][]
    for (let i = 0; i < coords.length - 2; i++) {
      const [lng1, lat1] = coords[i]
      const [lng2, lat2] = coords[i + 1]
      const [lng3, lat3] = coords[i + 2]
      const angle = computeTurnAngle(lat1, lng1, lat2, lng2, lat3, lng3)
      if (angle >= 90) {
        showToast(t("map_road_turn_too_sharp"), "error")
        cleanup()
        return false
      }
    }
    // Whole-road turn: even if every individual corner is gentle, a road whose
    // overall direction changes by 90° or more is still a sharp turn.
    if (coords.length >= 3) {
      const [flng1, flat1] = coords[0]
      const [flng2, flat2] = coords[1]
      const [llng1, llat1] = coords[coords.length - 2]
      const [llng2, llat2] = coords[coords.length - 1]
      // Compare the first and last segment vectors directly.
      const v1x = flng2 - flng1
      const v1y = flat2 - flat1
      const v2x = llng2 - llng1
      const v2y = llat2 - llat1
      const len1 = Math.hypot(v1x, v1y)
      const len2 = Math.hypot(v2x, v2y)
      if (len1 > 1e-10 && len2 > 1e-10) {
        const dot = (v1x * v2x + v1y * v2y) / (len1 * len2)
        const overallAngle = (Math.acos(Math.min(1, Math.max(-1, dot))) * 180) / Math.PI
        if (overallAngle >= 90) {
          showToast(t("map_road_turn_too_sharp"), "error")
          cleanup()
          return false
        }
      }
    }
  }
  if (
    geometry.type === "Polygon" &&
    (!geometry.coordinates[0] || geometry.coordinates[0].length < 3)
  ) {
    showToast(t("map_area_min_points"), "error")
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
    // Shared rule — the modal validation in useFeatureValidation enforces the
    // same limits with inline errors instead of toasts.
    const radiusError = cityCenterRadiusError(radius)
    if (radiusError === "too_small") {
      showToast(t("map_city_center_too_small"), "error")
      cleanup()
      return false
    }
    if (radiusError === "too_large") {
      showToast(t("map_city_center_too_large"), "error")
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

  clearEdgeVisibilityPoll()
  try {
    await gm.disableDraw()
  } catch (err) {
    debugError("[DRAW-SAVE] reset disableDraw:", err)
  }
  void buildDrawControl({
    key: phase.key,
    drawType: phase.drawType,
    color: phase.color,
  })
  repatchMarker()
}
