import { PHASES } from "../../phases"
import { debugLog, debugError } from "../../utils/debug"
import type { FeatureData, FeatureDataByType, FeatureTypeKey, ModalResult } from "../../types"
import { closeRing, computeCircleRing } from "../rendering/geometry"

export type ApiSaveShape = { type: string; layer: string }

/** How a feature's shape is drawn — determines which GeoJSON geometry is built. */
export type GeometryKind = "marker" | "line" | "polygon" | "circle"

export type DerivedGeometry = GeoJSON.Point | GeoJSON.LineString | GeoJSON.Polygon

/**
 * Single source of truth for turning stored FeatureData back into GeoJSON.
 *
 * Used by the loader, edit-commit, undo restore and context-menu updates —
 * these four mappings previously drifted (e.g. edit-commit rendered a
 * radius-less circle's coordinates as a closed LineString while undo made it
 * a Polygon), so keep ALL of them on this one implementation:
 *  - marker                        → Point
 *  - circle with lat/lng/radius    → closed LineString ring around the center
 *  - coordinates + kind "line"     → LineString
 *  - coordinates otherwise         → closed Polygon
 *  - fallback                      → Point at lat/lng (0,0 when absent)
 */
export function featureDataToGeometry(
  data: Pick<FeatureData, "lat" | "lng" | "radius" | "coordinates">,
  kind: GeometryKind,
): DerivedGeometry {
  const point = (): GeoJSON.Point => ({
    type: "Point",
    coordinates: [data.lng ?? 0, data.lat ?? 0],
  })

  if (kind === "circle") {
    if (data.lat != null && data.lng != null && data.radius) {
      return {
        type: "LineString",
        coordinates: closeRing(computeCircleRing(data.lat, data.lng, data.radius)),
      }
    }
    // A circle that lost its radius degrades to its center point rather than
    // misinterpreting arbitrary vertex coordinates as a ring.
    return point()
  }

  if (data.coordinates && data.coordinates.length > 0) {
    const coords = data.coordinates.map((c) => [c.lng, c.lat] as [number, number])
    if (kind === "line") {
      return { type: "LineString", coordinates: coords }
    }
    return { type: "Polygon", coordinates: [closeRing(coords)] }
  }

  return point()
}

function extractCoords(geometry: GeoJSON.Geometry): { lat: number; lng: number }[] | null {
  switch (geometry.type) {
    case "Point":
      return [{ lat: geometry.coordinates[1], lng: geometry.coordinates[0] }]
    case "LineString":
      return geometry.coordinates.map((c) => ({ lat: c[1], lng: c[0] }))
    case "Polygon":
      return geometry.coordinates[0].map((c) => ({ lat: c[1], lng: c[0] }))
    case "MultiPolygon":
      debugLog("[SAVE] MultiPolygon flattened to single Polygon (first ring)")
      return geometry.coordinates[0][0].map((c) => ({ lat: c[1], lng: c[0] }))
    default:
      return null
  }
}

function modalResultToBase(modalResult: ModalResult): FeatureData {
  const type = modalResult.type as FeatureTypeKey
  const label = modalResult.label
  if (modalResult.type === "areas") {
    return {
      type,
      label,
      decisionNumber: modalResult.decisionNumber,
      decisionDate: modalResult.decisionDate,
      areaTypeKey: modalResult.areaTypeKey,
    } as FeatureData
  }
  if (modalResult.type === "districts") {
    return {
      type,
      label,
      decisionNumber: modalResult.decisionNumber,
      decisionDate: modalResult.decisionDate,
      districtTypeKey: modalResult.districtTypeKey,
    } as FeatureData
  }
  if (modalResult.type === "cityCenter") {
    return { type, label, radius: modalResult.radius } as FeatureData
  }
  if (modalResult.type === "roads") {
    return {
      type,
      label,
      decisionNumber: modalResult.decisionNumber,
      decisionDate: modalResult.decisionDate,
      roadTypeKey: modalResult.roadTypeKey,
    } as FeatureData
  }
  if (modalResult.type === "houseEntrances") {
    return {
      type,
      label,
      entranceTypeKey: modalResult.entranceTypeKey,
      roadDbId: modalResult.roadDbId,
      roadLabel: modalResult.roadLabel,
      side: modalResult.side,
      entranceNumber: modalResult.entranceNumber,
      mainEntranceDbId: modalResult.mainEntranceDbId,
      mainEntranceLabel: modalResult.mainEntranceLabel,
      bisNumber: modalResult.bisNumber,
    } as FeatureData
  }
  if (modalResult.type === "publicBuildings") {
    return {
      type,
      label,
      decisionNumber: modalResult.decisionNumber,
      decisionDate: modalResult.decisionDate,
      sectorKey: modalResult.sectorKey,
      buildingTypeKey: modalResult.buildingTypeKey,
    } as FeatureData
  }
  if (modalResult.type === "publicSpaces") {
    return {
      type,
      label,
      decisionNumber: modalResult.decisionNumber,
      decisionDate: modalResult.decisionDate,
      spaceTypeKey: modalResult.spaceTypeKey,
    } as FeatureData
  }
  return {
    type,
    label,
    decisionNumber: modalResult.decisionNumber,
    decisionDate: modalResult.decisionDate,
  } as FeatureData
}

export function buildFeatureData(
  geometry: GeoJSON.Geometry,
  phase: (typeof PHASES)[number],
  modalResult: ModalResult,
): FeatureDataByType {
  const base = modalResultToBase(modalResult)

  if (phase.key === "cityCenter") {
    base.radius = modalResult.type === "cityCenter" ? modalResult.radius : base.radius
  }

  const coords = extractCoords(geometry)

  if (!coords) {
    debugError("[SAVE] Unknown geometry type:", geometry.type, geometry)
    return base as FeatureDataByType
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

  return result as FeatureDataByType
}

export function toApiSaveShape(fd: FeatureData): ApiSaveShape {
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
      debugError("[SAVE] toApiSaveShape: unknown feature type:", fd.type)
      return { type: "unknown", layer: "unknown" }
  }
}
