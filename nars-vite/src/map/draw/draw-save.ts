// ─── DRAW SAVE ────────────────────────────────────────────────────────────────
// Handles saving a completed drawing to the database, updating the feature store,
// and resetting the draw mode for the next feature.

import { PHASES } from "../../phases"
import { syncCounts } from "../../store"
import { useLayerStore } from "../../stores/layerStore"
import type { LayerState } from "../../stores/layerStore"
import type { LayerEntry } from "../../types"
import { ctx, featuresStore } from "../core/state"
import { buildDrawControl } from "./draw-control"
import { refreshLayerVisibility } from "../rendering/labels"
import { computeCircleRing, computeCircleRadius, closeRing } from "../rendering/geometry"
import { getFeatureType } from "../house-numbering"
import { showToast } from "../../lib/toast"
import { debugError } from "../../utils/debug"
import type { ModalResult } from "../../types"
import { updateEndpointMarkers } from "../roads/road-directions"
import { buildFeatureData, saveToDatabase } from "../features/features"
import { openModalForFeature } from "./draw-modal"
import { getDrawingPhase, setSavingFeature, repatchMarker } from "./draw-state"
import { areaStyle } from "../rendering/styles"

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
    style.fillColor = "#f39c12"
    style.fillOpacity = 0
    style.lineColor = "#f39c12"
    style.lineWidth = 3
  } else if (phase.key === "publicBuildings") {
    style.fillColor = "#e67e22"
    style.fillOpacity = 0.25
    style.lineColor = "#e67e22"
    style.lineWidth = 3
  } else if (phase.key === "publicSpaces") {
    style.fillColor = "#2ecc71"
    style.fillOpacity = 0.2
    style.lineColor = "#2ecc71"
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
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  geomanFeatureData: any,
): Promise<boolean> {
  if (getDrawingPhase()?.key !== "cityCenter") return true
  const layerStore = useLayerStore()
  const state = layerStore.$state as LayerState
  if ((state.cityCenter?.length ?? 0) === 0) return true

  showToast("A city center already exists. Delete it first to create a new one.", "error")
  try {
    geomanFeatureData?.delete?.()
  } catch (err) {
    debugError("[DRAW-SAVE] delete:", err)
  }
  return false
}

// ─── SAVE & UPDATE STORE ──────────────────────────────────────────

async function saveAndUpdateStore(
  geometry: GeoJSON.Geometry,
  drawingPhase: (typeof PHASES)[number],
  modalResult: ModalResult,
  narsDrawType: string,
  featureId: string,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  geomanFeatureData: any,
): Promise<void> {
  setSavingFeature(true)
  try {
    const featureData = buildFeatureData(geometry, drawingPhase, modalResult)
    const saveResult = await saveToDatabase(featureData)
    if (!saveResult.ok || !saveResult.data) {
      showToast("Save failed: " + (saveResult.error ?? "Please try again."), "error")
      return
    }

    const dbId = saveResult.data.id
    let style = getFeatureStyle(drawingPhase, modalResult)
    let storeGeometry = normalizeGeometry(geometry, drawingPhase.drawType)

    if (drawingPhase.key === "cityCenter" && featureData.radius) {
      const ring = closeRing(
        computeCircleRing(featureData.lat!, featureData.lng!, featureData.radius),
      )
      storeGeometry = { type: "LineString", coordinates: ring }
      style = {
        lineColor: "#e74c3c",
        lineWidth: 6,
        textColor: "#333333",
        radius: featureData.radius,
      }
    }

    featuresStore.add({
      id: featureId,
      geometry: storeGeometry,
      properties: {
        dbId,
        phaseKey: drawingPhase.key,
        label: featureData.label,
        geomType: storeGeometry.type,
        ...style,
      },
    })

    const layerStore = useLayerStore()
    const phaseKey = drawingPhase.key as keyof LayerState
    ;(layerStore.$state[phaseKey] as LayerEntry[]).push({
      id: featureId,
      dbId,
      data: featureData,
      type: getFeatureType(narsDrawType),
    })

    syncCounts()
    refreshLayerVisibility()
    if (drawingPhase.key === "roads") updateEndpointMarkers()
    showToast("Feature saved.", "success")

    setTimeout(() => {
      try {
        geomanFeatureData?.delete?.()
      } catch (err) {
        debugError("[DRAW-SAVE] deferred delete:", err)
      }
    }, 100)
  } catch (err) {
    debugError("[COMPLETE] Save error:", err)
    showToast("Save failed: " + (err as Error).message, "error")
  } finally {
    setSavingFeature(false)
    setTimeout(() => resetDrawMode(), 200)
  }
}

// ─── COMPLETE DRAWING ─────────────────────────────────────────────────────────

export async function completeDrawingWithGeometry(
  geometry: GeoJSON.Geometry,
  narsDrawType: string,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  geomanFeatureData: any,
): Promise<void> {
  const drawingPhase = getDrawingPhase()
  if (!drawingPhase) return

  if (!(await checkExistingCityCenter(geomanFeatureData))) return
  if (!validateGeometry(geometry, drawingPhase, geomanFeatureData)) return

  const featureId = crypto.randomUUID()
  const gm = ctx.geoman
  if (gm) {
    try {
      await gm.disableDraw()
    } catch (err) {
      debugError("[DRAW-SAVE] disableDraw:", err)
    }
  }

  const modalResult = await openModalForFeature(drawingPhase, featureId, geometry)
  if (!modalResult) {
    try {
      geomanFeatureData?.delete?.()
    } catch (err) {
      debugError("[DRAW-SAVE] modal delete:", err)
    }
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
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  geomanFeatureData: any,
): boolean {
  const cleanup = () => {
    try {
      geomanFeatureData?.delete?.()
    } catch (err) {
      debugError("[VALIDATE] cleanup:", err)
    }
  }

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
    if (!radius || Number.isNaN(radius) || radius < 5) {
      showToast("City center radius is too small (minimum 5 meters).", "error")
      cleanup()
      return false
    }
    if (radius > 50000) {
      showToast("City center radius is too large (maximum 50 km).", "error")
      cleanup()
      return false
    }
  }
  return true
}

// ─── DRAW MODE RESET ──────────────────────────────────────────────────────────

async function resetDrawMode(): Promise<void> {
  const gm = ctx.geoman
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
