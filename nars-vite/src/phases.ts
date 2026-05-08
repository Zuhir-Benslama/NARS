// ─── PHASE PIPELINE ───────────────────────────────────────────────────
// Defines the 8 mapping phases and the API-layer → phase-key lookup table.
//
// Feature sub-type data (AREA_TYPES, DISTRICT_TYPES, ROAD_TYPES, etc.) has been
// moved to types/feature-types.ts. They are re-exported here for backward compatibility
// so existing imports don't need updating.

import type { Phase } from "./types"

export type { AreaType, DistrictType, RoadType, PublicSpaceType } from "./types/feature-types"
export {
  AREA_TYPES,
  DISTRICT_TYPES,
  ROAD_TYPES,
  PUBLIC_SPACE_TYPES,
  PUBLIC_BUILDING_SECTORS,
} from "./types/feature-types"

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
// Maps every possible value of the `layer` column in the DB back to the phase
// key used in featureLayers. Used by loader.ts when hydrating saved features.

export const API_LAYER_TO_PHASE: Record<string, string> = {
  // Areas
  central_urban: "areas",
  secondary_urban: "areas",
  // City center
  city_center: "cityCenter",
  // Districts
  housing_estate: "districts",
  urban_pole: "districts",
  district: "districts",
  trad_activities_zone: "districts",
  industry_zone: "districts",
  // Roads
  boulevard: "roads",
  avenue: "roads",
  street: "roads",
  drive: "roads",
  lane: "roads",
  cul_de_sac: "roads",
  way: "roads", // Legacy key for backward compatibility with old feature data
  // House entrances
  main_entrance: "houseEntrances",
  secondary_entrance: "houseEntrances",
  // Public buildings (top-level type key + all sub-type keys)
  public_building: "publicBuildings",
  bank: "publicBuildings",
  post_office: "publicBuildings",
  convention_centre: "publicBuildings",
  public_market: "publicBuildings",
  trade_centre: "publicBuildings",
  library: "publicBuildings",
  museum: "publicBuildings",
  theater: "publicBuildings",
  borders_guard: "publicBuildings",
  customs: "publicBuildings",
  fire_station: "publicBuildings",
  gendarmes: "publicBuildings",
  military_barrack: "publicBuildings",
  police_station: "publicBuildings",
  administrative_branch: "publicBuildings",
  public_hospital: "publicBuildings",
  neighborhood_health: "publicBuildings",
  specialized_hospital: "publicBuildings",
  treatment_room: "publicBuildings",
  university_hospital: "publicBuildings",
  research_institute: "publicBuildings",
  university: "publicBuildings",
  college: "publicBuildings",
  school: "publicBuildings",
  cemetery: "publicBuildings",
  mosque: "publicBuildings",
  hostel: "publicBuildings",
  hotel: "publicBuildings",
  motel: "publicBuildings",
  airport: "publicBuildings",
  bus_station: "publicBuildings",
  train_station: "publicBuildings",
  specialized_vocational_institute: "publicBuildings",
  vocational_education_institute: "publicBuildings",
  vocational_apprenticeship_center: "publicBuildings",
  vocational_training_institute: "publicBuildings",
  indoor_arena: "publicBuildings",
  leisure_center: "publicBuildings",
  sports_complex: "publicBuildings",
  stadium: "publicBuildings",
  swimming_pool: "publicBuildings",
  youth_clubs: "publicBuildings",
  youth_hostel: "publicBuildings",
  // Public spaces
  garden: "publicSpaces",
  square: "publicSpaces",
  // Naming panels
  naming_panel: "namingPanels",
}
