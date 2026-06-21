import { PHASES } from "../../phases"
import { debugLog, debugError } from "../../utils/debug"
import type { FeatureData, ModalResult } from "../../types"

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
