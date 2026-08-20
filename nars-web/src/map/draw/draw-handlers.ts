// ─── DRAW EVENT HANDLERS ──────────────────────────────────────────────────────
// Core event handlers: gm:create, contextmenu, click, keyboard (ESC, Ctrl+Z).

import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useModalStore } from "../../stores/modalStore"
import { useSelectionStore } from "../../stores/selectionStore"
import { debugError } from "../../utils/debug"
import { repatchMarker } from "./draw-state"
import type { GeomanCreateEvent, ActionInstances } from "../core/geoman-types"

const POINT_SNAP_THRESHOLD_PX = 20
import type { MapMouseEvent as MapLibreMapMouseEvent } from "maplibre-gl"

import { getCtx, updateSelectionHighlight } from "../core/state"
import { useFeaturesStore } from "../../stores/featuresStore"
import { showContextMenu, showMapContextMenu } from "../context-menu/context-menu"
import { buildDrawControl, clearEdgeVisibilityPoll } from "./draw-control"
import {
  getDrawingPhase,
  completeDrawingWithGeometry,
  isSavingFeature,
  removeLastVertex,
} from "./draw-complete"
import { isEditMode, commitEditMode, cancelEditMode } from "../edit/edit-mode"
import { computeCircleRadius, computeCircleCenter } from "../rendering/geometry"
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
      const { lat: centerLat, lng: centerLng } = computeCircleCenter(coords)
      const radius = computeCircleRadius(centerLat, centerLng, coords)

      geometry = { type: "Point" as const, coordinates: [centerLng, centerLat] }
      ;(geometry as GeoJSON.Point & { radius: number }).radius = radius
    }
  } else if (shape === "polygon" && geoJson.geometry.type === "MultiPolygon") {
    const mp = geoJson.geometry
    if (mp.coordinates.length > 0 && mp.coordinates[0].length > 0) {
      geometry = {
        type: "Polygon" as const,
        coordinates: mp.coordinates[0],
      }
    }
  }

  try {
    await completeDrawingWithGeometry(geometry, narsDrawType, featureData)
  } catch (err) {
    debugError("[GM:CREATE] Error:", err)
  }
}

// ─── NEAREST FEATURE LOOKUP ────────────────────────────────────────────────────

function findNearestFeatureAtPoint(
  px: number,
  py: number,
): { dbId: string; phaseKey: string } | null {
  const { map } = getCtx()
  const featuresStore = useFeaturesStore()
  const allFeatures = featuresStore.getAll()
  let nearestDbId: string | null = null
  let nearestPhaseKey: string | null = null
  let nearestDist = POINT_SNAP_THRESHOLD_PX

  for (const f of allFeatures) {
    const fPhaseKey = f.properties?.phaseKey
    const fDbId = f.properties?.dbId
    if (!fDbId || !fPhaseKey) continue

    if (f.geometry.type === "Point") {
      const point = map.project([f.geometry.coordinates[0], f.geometry.coordinates[1]])
      const dist = Math.sqrt((point.x - px) ** 2 + (point.y - py) ** 2)
      if (dist < nearestDist) {
        nearestDist = dist
        nearestDbId = fDbId
        nearestPhaseKey = fPhaseKey
      }
    } else if (f.geometry.type === "LineString") {
      const coords = f.geometry.coordinates as [number, number][]
      for (let i = 0; i < coords.length - 1; i++) {
        const p1 = map.project([coords[i][0], coords[i][1]])
        const p2 = map.project([coords[i + 1][0], coords[i + 1][1]])
        const dist = pointToSegmentDist(px, py, p1.x, p1.y, p2.x, p2.y)
        if (dist < nearestDist) {
          nearestDist = dist
          nearestDbId = fDbId
          nearestPhaseKey = fPhaseKey
        }
      }
    } else if (f.geometry.type === "Polygon") {
      const rings = (f.geometry as GeoJSON.Polygon).coordinates
      for (const ring of rings) {
        for (let i = 0; i < ring.length - 1; i++) {
          const p1 = map.project(ring[i] as [number, number])
          const p2 = map.project(ring[i + 1] as [number, number])
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

  return nearestDbId && nearestPhaseKey ? { dbId: nearestDbId, phaseKey: nearestPhaseKey } : null
}

// ─── CONTEXT MENU HANDLER ─────────────────────────────────────────────────────

function onContextMenu(e: MouseEvent): void {
  const appStore = useAppStore()
  const { map, geoman } = getCtx()
  const mapEl = map.getContainer()
  if (!mapEl.contains(e.target as Node)) return

  if (isEditMode()) {
    e.preventDefault()
    commitEditMode().catch((err) => debugError("[CONTEXT] commitEditMode:", err))
    return
  }

  const actionInstances = geoman?.actionInstances as ActionInstances | undefined
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
      clearEdgeVisibilityPoll()
      geoman
        ?.disableDraw()
        .then(() => {
          if (phase && phase.key !== "namingPanels") {
            buildDrawControl(phase)
            repatchMarker()
          }
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

  const rect = map.getContainer().getBoundingClientRect()
  const px = e.clientX - rect.left
  const py = e.clientY - rect.top

  const features = map.queryRenderedFeatures([px, py] as [number, number])
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
    const nearest = findNearestFeatureAtPoint(px, py)
    if (nearest) {
      showContextMenu(e.clientX, e.clientY, nearest.dbId, nearest.phaseKey)
    } else {
      showMapContextMenu(e.clientX, e.clientY, phase)
    }
  }
}

// ─── CLICK HANDLER ────────────────────────────────────────────────────────────

function onClick(e: MapLibreMapMouseEvent & { point: { x: number; y: number } }): void {
  const appStore = useAppStore()
  if (isEditMode()) return
  const { geoman, map } = getCtx()
  if (geoman && geoman.getActiveDrawModes?.().length > 0) return

  const phase = PHASES[appStore.currentPhase]
  const features = map.queryRenderedFeatures(e.point)

  let feature
  if (phase?.key === "cityCenter") {
    feature = features.find(
      (f) => f.source === "features" && f.properties?.phaseKey === "cityCenter",
    )
  } else {
    feature = features.find((f) => f.source === "features")
  }

  const selectionStore = useSelectionStore()

  if (feature) {
    const dbId = feature.properties?.dbId
    if (dbId) {
      selectionStore.setSelectedFeatureDbId(dbId)
      updateSelectionHighlight(dbId)
    }
  } else {
    selectionStore.setSelectedFeatureDbId(null)
    updateSelectionHighlight(null)
    const currentPhase = PHASES[appStore.currentPhase]
    if (currentPhase && currentPhase.key !== "namingPanels") {
      buildDrawControl(currentPhase)
    }
  }
}

// ─── KEYBOARD HANDLERS ────────────────────────────────────────────────────────

function onKeyDown(e: KeyboardEvent): void {
  // Do not hijack Ctrl+Z / Escape while the user is typing in an input,
  // textarea or contenteditable (label editing, modal fields). Native undo
  // and field behavior must win there.
  const target = e.target as HTMLElement | null
  if (
    target &&
    (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable)
  ) {
    return
  }

  const appStore = useAppStore()
  if (e.key === "Escape") {
    if (useModalStore().visible) return

    const geoman = getCtx().geoman
    const drawing = (geoman?.getActiveDrawModes?.().length ?? 0) > 0
    if (drawing) {
      e.preventDefault()
      e.stopImmediatePropagation()
      const phase = PHASES[appStore.currentPhase]
      clearEdgeVisibilityPoll()
      geoman
        ?.disableDraw()
        .then(() => {
          if (phase && phase.key !== "namingPanels") {
            buildDrawControl(phase)
            repatchMarker()
          }
        })
        .catch((err) => debugError("[KEYDOWN] disableDraw:", err))
      return
    }
    if (isEditMode()) {
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

let contextMenuCleanup: (() => void) | null = null
let keydownCleanup: (() => void) | null = null
let mapGmCreateCleanup: (() => void) | null = null
let mapClickCleanup: (() => void) | null = null

export function registerDrawHandlers(): void {
  const map = getCtx().map

  map.on("gm:create", onFeatureCreated)
  mapGmCreateCleanup = () => map.off("gm:create", onFeatureCreated)

  window.addEventListener("contextmenu", onContextMenu, true)
  contextMenuCleanup = () => window.removeEventListener("contextmenu", onContextMenu, true)

  map.on("click", onClick)
  mapClickCleanup = () => map.off("click", onClick)

  document.addEventListener("keydown", onKeyDown, true)
  keydownCleanup = () => document.removeEventListener("keydown", onKeyDown, true)
}

export function destroyDrawHandlers(): void {
  mapGmCreateCleanup?.()
  mapGmCreateCleanup = null
  contextMenuCleanup?.()
  contextMenuCleanup = null
  mapClickCleanup?.()
  mapClickCleanup = null
  keydownCleanup?.()
  keydownCleanup = null
}
