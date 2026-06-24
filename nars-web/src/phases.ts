// ─── PHASE PIPELINE ───────────────────────────────────────────────────
// Defines the 8 mapping phases and the API-layer → phase-key lookup table.
//
// Feature sub-type data (AREA_TYPES, DISTRICT_TYPES, ROAD_TYPES, etc.) has been
// moved to types/feature-types.ts. They are re-exported here for backward compatibility
// so existing imports don't need updating.

import type { Phase } from "./types"

import {
  AREA_TYPES,
  DISTRICT_TYPES,
  ROAD_TYPES,
  PUBLIC_SPACE_TYPES,
  PUBLIC_BUILDING_SECTORS,
} from "./types/feature-types"

export type { AreaType, DistrictType, RoadType, PublicSpaceType } from "./types/feature-types"
export { AREA_TYPES, DISTRICT_TYPES, ROAD_TYPES, PUBLIC_SPACE_TYPES, PUBLIC_BUILDING_SECTORS }

// ─── PHASES ───────────────────────────────────────────────────────────

export const PHASES: Phase[] = [
  {
    index: 0,
    key: "areas",
    label: "phase_areas_label",
    drawType: "polygon",
    color: "#8e44ad",
    hint: "phase_areas_hint",
    geometryType: "Polygon",
  },
  {
    index: 1,
    key: "districts",
    label: "phase_districts_label",
    drawType: "polygon",
    color: "#f39c12",
    hint: "phase_districts_hint",
    geometryType: "Polygon",
  },
  {
    index: 2,
    key: "cityCenter",
    label: "phase_cityCenter_label",
    drawType: "circle",
    color: "#e74c3c",
    hint: "phase_cityCenter_hint",
    geometryType: "Point",
  },
  {
    index: 3,
    key: "roads",
    label: "phase_roads_label",
    drawType: "polyline",
    color: "#3498db",
    hint: "phase_roads_hint",
    geometryType: "LineString",
  },
  {
    index: 4,
    key: "houseEntrances",
    label: "phase_houseEntrances_label",
    drawType: "marker",
    color: "#27ae60",
    hint: "phase_houseEntrances_hint",
    geometryType: "Point",
  },
  {
    index: 5,
    key: "publicBuildings",
    label: "phase_publicBuildings_label",
    drawType: "polygon",
    color: "#e67e22",
    hint: "phase_publicBuildings_hint",
    geometryType: "Polygon",
  },
  {
    index: 6,
    key: "publicSpaces",
    label: "phase_publicSpaces_label",
    drawType: "polygon",
    color: "#2ecc71",
    hint: "phase_publicSpaces_hint",
    geometryType: "Polygon",
  },
  {
    index: 7,
    key: "namingPanels",
    label: "phase_namingPanels_label",
    drawType: "marker",
    color: "#9b59b6",
    hint: "phase_namingPanels_hint",
    geometryType: "Point",
  },
]

// ─── API_LAYER_TO_PHASE ───────────────────────────────────────────────
// Auto-generated from feature-types.ts — no manual sync needed when
// adding new sub-types. Maps every DB layer value to its phase key.

function buildApiLayerToPhase(): Record<string, string> {
  const map: Record<string, string> = {}

  for (const t of AREA_TYPES) map[t.key] = "areas"
  map.city_center = "cityCenter"
  for (const t of DISTRICT_TYPES) map[t.key] = "districts"
  for (const t of ROAD_TYPES) map[t.key] = "roads"
  map.main_entrance = "houseEntrances"
  map.secondary_entrance = "houseEntrances"
  map.public_building = "publicBuildings"
  for (const sector of PUBLIC_BUILDING_SECTORS) {
    for (const b of sector.buildings) map[b.key] = "publicBuildings"
  }
  for (const t of PUBLIC_SPACE_TYPES) map[t.key] = "publicSpaces"
  map.naming_panel = "namingPanels"

  return map
}

export const API_LAYER_TO_PHASE: Record<string, string> = buildApiLayerToPhase()
