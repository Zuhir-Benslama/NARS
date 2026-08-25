// ─── FEATURE SUB-TYPE DEFINITIONS ─────────────────────────────────────
// All per-phase feature type lists: area types, district types, road types,
// public space types, and public building sector/type hierarchies.
//
// Importers: FeatureModal.vue, map/styles.ts, map/labels.ts, map/create-handler.ts────────

export interface AreaType {
  key: string
  label: string
  color: string
}

export interface DistrictType {
  key: string
  label: string
  allowInScattered?: boolean
}

export interface RoadType {
  key: string
  label: string
  category: "primary" | "secondary" | "tertiary"
}

export interface PublicSpaceType {
  key: string
  label: string
  color: string
}

// NOTE: central_urban uses #c0392b (red) to visually distinguish it as the
// primary area type. This intentionally differs from the areas phase color
// (#8e44ad in phases.ts) which is used for phase navigation UI only.
// secondary_urban uses #8e44ad matching the areas phase color.
export const AREA_TYPES: AreaType[] = [
  { key: "central_urban", label: "Main Urban Area", color: "#c0392b" },
  { key: "secondary_urban", label: "Secondary Urban Area", color: "#8e44ad" },
]

// ── District types ────────────────────────────────────────────────────

export const DISTRICT_TYPES: DistrictType[] = [
  { key: "housing_estate", label: "Housing Estate" },
  { key: "urban_pole", label: "Urban Pole" },
  { key: "district", label: "District" },
  { key: "trad_activities_zone", label: "Trad. Activities Zone" },
  { key: "industry_zone", label: "Industry Zone", allowInScattered: true },
]

// ── Road types ────────────────────────────────────────────────────────

export const ROAD_TYPES: RoadType[] = [
  { key: "boulevard", label: "Boulevard", category: "primary" },
  { key: "avenue", label: "Avenue", category: "primary" },
  { key: "street", label: "Street", category: "secondary" },
  { key: "drive", label: "Drive", category: "tertiary" },
  { key: "lane", label: "Lane", category: "tertiary" },
  { key: "cul_de_sac", label: "Cul-de-sac", category: "tertiary" },
  { key: "way", label: "Way", category: "tertiary" },
]

// ── Public space types ────────────────────────────────────────────────

export const PUBLIC_SPACE_TYPES: PublicSpaceType[] = [
  { key: "garden", label: "Garden", color: "#27ae60" },
  { key: "square", label: "Square", color: "#2980b9" },
]

// ── Public building sector / type hierarchy ───────────────────────────

export interface PublicBuildingType {
  key: string
  label: string
}

export interface PublicBuildingSector {
  key: string
  label: string
  buildings: PublicBuildingType[]
}

export const PUBLIC_BUILDING_SECTORS: PublicBuildingSector[] = [
  {
    key: "banking_postal",
    label: "Banking & Postal",
    buildings: [
      { key: "bank", label: "Bank" },
      { key: "post_office", label: "Post Offices" },
    ],
  },
  {
    key: "commerce",
    label: "Commerce",
    buildings: [
      { key: "convention_centre", label: "Convention Centres" },
      { key: "public_market", label: "Public Markets" },
      { key: "trade_centre", label: "Trade Centres" },
    ],
  },
  {
    key: "culture",
    label: "Culture",
    buildings: [
      { key: "library", label: "Libraries" },
      { key: "museum", label: "Museums" },
      { key: "theater", label: "Theaters" },
    ],
  },
  {
    key: "defence_security",
    label: "Defence and Security",
    buildings: [
      { key: "borders_guard", label: "Borders Guard Unit" },
      { key: "customs", label: "Customs Unit" },
      { key: "fire_station", label: "Fire Station Unit" },
      { key: "gendarmes", label: "Gendarmes Unit" },
      { key: "military_barrack", label: "Military Barrack" },
      { key: "police_station", label: "Police Station" },
    ],
  },
  {
    key: "government_law",
    label: "Government & Law",
    buildings: [{ key: "administrative_branch", label: "Administrative Branch" }],
  },
  {
    key: "healthcare",
    label: "Healthcare",
    buildings: [
      { key: "public_hospital", label: "Public Hospital Establishment" },
      { key: "neighborhood_health", label: "Public Neighborhood Health Establishment" },
      { key: "specialized_hospital", label: "Specialized Hospital Establishment" },
      { key: "treatment_room", label: "Treatment Room" },
      { key: "university_hospital", label: "University Hospital Center" },
    ],
  },
  {
    key: "higher_education",
    label: "Higher Education",
    buildings: [
      { key: "research_institute", label: "Research Institute" },
      { key: "university", label: "University" },
    ],
  },
  {
    key: "national_education",
    label: "National Education",
    buildings: [
      { key: "college", label: "College" },
      { key: "school_library", label: "Libraries" },
      { key: "school", label: "School" },
    ],
  },
  {
    key: "religious",
    label: "Religious",
    buildings: [
      { key: "cemetery", label: "Cemetery" },
      { key: "mosque", label: "Mosque" },
    ],
  },
  {
    key: "tourism",
    label: "Tourism",
    buildings: [
      { key: "hostel", label: "Hostel" },
      { key: "hotel", label: "Hotel" },
      { key: "motel", label: "Motel" },
    ],
  },
  {
    key: "transport",
    label: "Transport",
    buildings: [
      { key: "airport", label: "Airport" },
      { key: "bus_station", label: "Bus Station" },
      { key: "train_station", label: "Train Station" },
    ],
  },
  {
    key: "vocational_training",
    label: "Vocational Training and Education",
    buildings: [
      {
        key: "specialized_vocational_institute",
        label: "National Specialized Vocational Training Institute",
      },
      { key: "vocational_education_institute", label: "Vocational Education Institute" },
      {
        key: "vocational_apprenticeship_center",
        label: "Vocational Training and Apprenticeship Center",
      },
      { key: "vocational_training_institute", label: "Vocational Training Institute" },
    ],
  },
  {
    key: "youth_sports",
    label: "Youth & Sports",
    buildings: [
      { key: "indoor_arena", label: "Indoor Arena" },
      { key: "leisure_center", label: "Leisure Center" },
      { key: "sports_complex", label: "Sports Complex" },
      { key: "stadium", label: "Stadium" },
      { key: "swimming_pool", label: "Swimming Pool" },
      { key: "youth_clubs", label: "Youth Clubs" },
      { key: "youth_hostel", label: "Youth Hostel" },
    ],
  },
]
