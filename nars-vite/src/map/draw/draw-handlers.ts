// ─── DRAW EVENT HANDLERS ──────────────────────────────────────────────────────
// Core event handlers: gm:create, contextmenu, click, keyboard (ESC, Ctrl+Z).

import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useModalStore } from "../../stores/modalStore"
import { setSelectedFeature } from "../../stores"
import { debugError } from "../../utils/debug"
import type { GeomanCreateEvent, ActionInstances } from "../core/geoman-types"
import type { MapMouseEvent as MapLibreMapMouseEvent } from "maplibre-gl"

import { ctx, updateSelectionHighlight, featuresStore } from "../core/state"
import { showContextMenu, showMapContextMenu } from "../context-menu/context-menu"
import { buildDrawControl } from "./draw-control"
import {
  getDrawingPhase,
  completeDrawingWithGeometry,
  isSavingFeature,
  removeLastVertex,
} from "./draw-complete"
import { isEditMode, commitEditMode, cancelEditMode } from "../edit/edit-mode"
import { computeCircleRadius } from "../rendering/geometry"
import { undo } from "../undo"

// ─── GEOMETRY HELPER ──────────────────────────────────────────────────────────

export function pointToSegmentDist(
  px: number,
  py: number,
  x1: number,
  y1: number,
  x2: number,
  y2: number,
): number {
  const dx = x2 - x1
  const dy = y2 - y1
  const lenSq = dx * dx + dy * dy
  if (lenSq === 0) return Math.sqrt((px - x1) ** 2 + (py - y1) ** 2)
  const t = Math.max(0, Math.min(1, ((px - x1) * dx + (py - y1) * dy) / lenSq))
  const nearX = x1 + t * dx
  const nearY = y1 + t * dy
  return Math.sqrt((px - nearX) ** 2 + (py - nearY) ** 2)
}

// ─── GM:CREATE HANDLER ────────────────────────────────────────────────────────

async function onFeatureCreated(e: GeomanCreateEvent): Promise<void> {
  if (isSavingFeature()) return

  const featureData = e.featureData || e.feature
  if (!featureData) return
  const shape = e.shape || (featureData as { shape?: string }).shape

  const geoJson = featureData.getGeoJson?.() || featureData._geoJson
  if (!geoJson?.geometry) return

  const shapeToDrawType: Record<string, string> = {
    polygon: "polygon",
    line: "line",
    marker: "marker",
    circle: "circle",
  }
  const drawingPhase = getDrawingPhase()
  const narsDrawType =
    (shape ? shapeToDrawType[shape] : undefined) ?? drawingPhase?.drawType ?? "polygon"

  let geometry = geoJson.geometry

  // Circle: convert Polygon to Point with radius
  if (shape === "circle" && geoJson.geometry.type === "Polygon") {
    const coords = geoJson.geometry.coordinates[0] as [number, number][]

    if (coords.length >= 3) {
      let sumLat = 0,
        sumLng = 0
      for (const [lng, lat] of coords) {
        sumLat += lat
        sumLng += lng
      }
      const centerLat = sumLat / coords.length
      const centerLng = sumLng / coords.length
      const radius = computeCircleRadius(centerLat, centerLng, coords)

      geometry = { type: "Point", coordinates: [centerLng, centerLat] } as GeoJSON.Point
      ;(geometry as GeoJSON.Point & { radius: number }).radius = radius
    }
  } else if (shape === "polygon" && geoJson.geometry.type === "MultiPolygon") {
    const mp = geoJson.geometry as GeoJSON.MultiPolygon
    if (mp.coordinates.length > 0 && mp.coordinates[0].length > 0) {
      geometry = {
        type: "Polygon",
        coordinates: mp.coordinates[0],
      } as unknown as GeoJSON.Polygon
    }
  }

  try {
    await completeDrawingWithGeometry(
      geometry as GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon,
      narsDrawType,
      featureData,
    )
  } catch (err) {
    debugError("[GM:CREATE] Error:", err)
  }
}

// ─── CONTEXT MENU HANDLER ─────────────────────────────────────────────────────

function onContextMenu(e: MouseEvent): void {
  const appStore = useAppStore()
  const mapEl = ctx.map.getContainer()
  if (!mapEl.contains(e.target as Node)) return

  if (isEditMode) {
    e.preventDefault()
    commitEditMode().catch((err) => debugError("[CONTEXT] commitEditMode:", err))
    return
  }

  const actionInstances = ctx.geoman?.actionInstances as ActionInstances | undefined
  const polygonInst = actionInstances?.["draw__polygon"]
  const lineInst = actionInstances?.["draw__line"]
  const drawInstance = polygonInst ?? lineInst
  const lineDrawer = drawInstance?.lineDrawer
  const midDraw = lineDrawer?.shapeLngLats && lineDrawer.shapeLngLats.length > 0

  if (midDraw) {
    e.preventDefault()
    e.stopPropagation()
    const coords: [number, number][] = lineDrawer.shapeLngLats
    if (coords.length <= 1) {
      const phase = PHASES[appStore.currentPhase]
      ctx
        .geoman!.disableDraw()
        .then(() => {
          if (phase && phase.key !== "namingPanels") buildDrawControl(phase)
        })
        .catch((err) => debugError("[CONTEXT] disableDraw:", err))
      return
    }
    removeLastVertex().catch((err) => debugError("[CONTEXT] removeLastVertex:", err))
    return
  }

  e.preventDefault()
  e.stopImmediatePropagation()
  const phase = PHASES[appStore.currentPhase]
  if (!phase) return

  const rect = ctx.map.getContainer().getBoundingClientRect()
  const px = e.clientX - rect.left
  const py = e.clientY - rect.top

  const features = ctx.map.queryRenderedFeatures([px, py] as [number, number])
  let feature
  if (phase.key === "cityCenter") {
    feature = features.find(
      (f) => f.source === "features" && f.properties?.phaseKey === "cityCenter",
    )
  } else {
    feature = features.find((f) => f.source === "features" && f.properties?.dbId)
  }

  if (feature && feature.properties?.dbId && feature.properties?.phaseKey) {
    showContextMenu(e.clientX, e.clientY, feature.properties.dbId, feature.properties.phaseKey)
  } else {
    const allFeatures = featuresStore.getAll()
    let nearestDbId: string | null = null
    let nearestPhaseKey: string | null = null
    let nearestDist = 20

    for (const f of allFeatures) {
      const fPhaseKey = f.properties?.phaseKey
      const fDbId = f.properties?.dbId
      if (!fDbId || !fPhaseKey) continue

      if (fPhaseKey === "roads" || fPhaseKey === "houseEntrances") {
        if (f.geometry.type === "Point") {
          const point = ctx.map.project([f.geometry.coordinates[0], f.geometry.coordinates[1]])
          const dist = Math.sqrt((point.x - px) ** 2 + (point.y - py) ** 2)
          if (dist < nearestDist) {
            nearestDist = dist
            nearestDbId = fDbId
            nearestPhaseKey = fPhaseKey
          }
        }
        if (f.geometry.type === "LineString") {
          const coords = f.geometry.coordinates
          for (let i = 0; i < coords.length - 1; i++) {
            const p1 = ctx.map.project([coords[i][0], coords[i][1]])
            const p2 = ctx.map.project([coords[i + 1][0], coords[i + 1][1]])
            const dist = pointToSegmentDist(px, py, p1.x, p1.y, p2.x, p2.y)
            if (dist < nearestDist) {
              nearestDist = dist
              nearestDbId = fDbId
              nearestPhaseKey = fPhaseKey
            }
          }
        }
      }
    }

    if (nearestDbId && nearestPhaseKey) {
      showContextMenu(e.clientX, e.clientY, nearestDbId, nearestPhaseKey)
    } else {
      showMapContextMenu(e.clientX, e.clientY, phase)
    }
  }
}

// ─── CLICK HANDLER ────────────────────────────────────────────────────────────

function onClick(e: MapLibreMapMouseEvent & { point: { x: number; y: number } }): void {
  const appStore = useAppStore()
  if (isEditMode) return
  if (ctx.geoman && ctx.geoman.getActiveDrawModes?.().length > 0) return

  const phase = PHASES[appStore.currentPhase]
  const features = ctx.map.queryRenderedFeatures(e.point)

  let feature
  if (phase?.key === "cityCenter") {
    feature = features.find(
      (f) => f.source === "features" && f.properties?.phaseKey === "cityCenter",
    )
  } else {
    feature = features.find((f) => f.source === "features")
  }

  if (feature) {
    const dbId = feature.properties?.dbId
    if (dbId) {
      setSelectedFeature(dbId)
      updateSelectionHighlight(dbId)
    }
  } else {
    setSelectedFeature(null)
    updateSelectionHighlight(null)
    const currentPhase = PHASES[appStore.currentPhase]
    if (currentPhase && currentPhase.key !== "namingPanels") {
      buildDrawControl(currentPhase)
    }
  }
}

// ─── KEYBOARD HANDLERS ────────────────────────────────────────────────────────

function onKeyDown(e: KeyboardEvent): void {
  const appStore = useAppStore()
  if (e.key === "Escape") {
    if (useModalStore().visible) return

    const drawing = (ctx.geoman?.getActiveDrawModes?.().length ?? 0) > 0
    if (drawing) {
      e.preventDefault()
      e.stopImmediatePropagation()
      const phase = PHASES[appStore.currentPhase]
      ctx
        .geoman!.disableDraw()
        .then(() => {
          if (phase && phase.key !== "namingPanels") buildDrawControl(phase)
        })
        .catch((err) => debugError("[KEYDOWN] disableDraw:", err))
      return
    }
    if (isEditMode) {
      e.preventDefault()
      e.stopImmediatePropagation()
      cancelEditMode().catch((err) => debugError("[KEYDOWN] cancelEditMode:", err))
    }
  }

  if (e.key === "z" && (e.ctrlKey || e.metaKey)) {
    e.preventDefault()
    undo()
  }
}

// ─── REGISTRATION ─────────────────────────────────────────────────────────────

export function registerDrawHandlers(): void {
  const map = ctx.map

  map.on("gm:create", onFeatureCreated)

  window.addEventListener("contextmenu", onContextMenu, true)

  map.on("click", onClick)

  document.addEventListener("keydown", onKeyDown, true)
}
