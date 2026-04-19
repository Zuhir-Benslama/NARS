package com.nars.maplibre.data.model

/**
 * Feature sub-type definitions matching the web version (feature-types.ts)
 * 
 * These definitions are extracted from phases.ts in the web version
 * to maintain separation of concerns.
 */

// ── Area types ────────────────────────────────────────────────────────────────

data class AreaType(
    val key: String,
    val label: String,
    val color: String
)

val AREA_TYPES = listOf(
    AreaType(key = "central_urban", label = "Main Urban Area", color = "#c0392b"),
    AreaType(key = "secondary_urban", label = "Secondary Urban Area", color = "#8e44ad"),
)

// ── District types ────────────────────────────────────────────────────────────

data class DistrictType(
    val key: String,
    val label: String,
    val allowInScattered: Boolean = false
)

val DISTRICT_TYPES = listOf(
    DistrictType(key = "housing_estate", label = "Housing Estate"),
    DistrictType(key = "urban_pole", label = "Urban Pole"),
    DistrictType(key = "district", label = "District"),
    DistrictType(key = "trad_activities_zone", label = "Trad. Activities Zone"),
    DistrictType(key = "industry_zone", label = "Industry Zone", allowInScattered = true),
)

// ── Road types ────────────────────────────────────────────────────────────────

data class RoadType(
    val key: String,
    val label: String,
    val category: String // "primary", "secondary", "tertiary"
)

val ROAD_TYPES = listOf(
    RoadType(key = "boulevard", label = "Boulevard", category = "primary"),
    RoadType(key = "avenue", label = "Avenue", category = "primary"),
    RoadType(key = "street", label = "Street", category = "secondary"),
    RoadType(key = "drive", label = "Drive", category = "tertiary"),
    RoadType(key = "lane", label = "Lane", category = "tertiary"),
    RoadType(key = "cul_de_sac", label = "Cul-de-sac", category = "tertiary"),
    RoadType(key = "way", label = "Way", category = "tertiary"),
)

// ── Public space types ───────────────────────────────────────────────────────

data class PublicSpaceType(
    val key: String,
    val label: String,
    val color: String
)

val PUBLIC_SPACE_TYPES = listOf(
    PublicSpaceType(key = "garden", label = "Garden", color = "#27ae60"),
    PublicSpaceType(key = "square", label = "Square", color = "#2980b9"),
)

// ── Public building sector / type hierarchy ──────────────────────────────────

data class PublicBuildingType(
    val key: String,
    val label: String
)

data class PublicBuildingSector(
    val key: String,
    val label: String,
    val buildings: List<PublicBuildingType>
)

val PUBLIC_BUILDING_SECTORS = listOf(
    PublicBuildingSector(
        key = "banking_postal",
        label = "Banking & Postal",
        buildings = listOf(
            PublicBuildingType(key = "bank", label = "Bank"),
            PublicBuildingType(key = "post_office", label = "Post Offices"),
        )
    ),
    PublicBuildingSector(
        key = "commerce",
        label = "Commerce",
        buildings = listOf(
            PublicBuildingType(key = "convention_centre", label = "Convention Centres"),
            PublicBuildingType(key = "public_market", label = "Public Markets"),
            PublicBuildingType(key = "trade_centre", label = "Trade Centres"),
        )
    ),
    PublicBuildingSector(
        key = "culture",
        label = "Culture",
        buildings = listOf(
            PublicBuildingType(key = "library", label = "Libraries"),
            PublicBuildingType(key = "museum", label = "Museum"),
            PublicBuildingType(key = "theater", label = "Theaters"),
        )
    ),
    PublicBuildingSector(
        key = "defence_security",
        label = "Defence and Security",
        buildings = listOf(
            PublicBuildingType(key = "borders_guard", label = "Borders Guard Unit"),
            PublicBuildingType(key = "customs", label = "Customs Unit"),
            PublicBuildingType(key = "fire_station", label = "Fire Station Unit"),
            PublicBuildingType(key = "gendarmes", label = "Gendarmes Unit"),
            PublicBuildingType(key = "military_barrack", label = "Military Barrack"),
            PublicBuildingType(key = "police_station", label = "Police Station"),
        )
    ),
    PublicBuildingSector(
        key = "government_law",
        label = "Government & Law",
        buildings = listOf(
            PublicBuildingType(key = "administrative_branch", label = "Administrative Branch"),
        )
    ),
    PublicBuildingSector(
        key = "healthcare",
        label = "Healthcare",
        buildings = listOf(
            PublicBuildingType(key = "public_hospital", label = "Public Hospital Establishment"),
            PublicBuildingType(key = "neighborhood_health", label = "Public Neighborhood Health Establishment"),
            PublicBuildingType(key = "specialized_hospital", label = "Specialized Hospital Establishment"),
            PublicBuildingType(key = "treatment_room", label = "Treatment Room"),
            PublicBuildingType(key = "university_hospital", label = "University Hospital Center"),
        )
    ),
    PublicBuildingSector(
        key = "higher_education",
        label = "Higher Education",
        buildings = listOf(
            PublicBuildingType(key = "research_institute", label = "Research Institute"),
            PublicBuildingType(key = "university", label = "University"),
        )
    ),
    PublicBuildingSector(
        key = "national_education",
        label = "National Education",
        buildings = listOf(
            PublicBuildingType(key = "college", label = "College"),
            PublicBuildingType(key = "library", label = "Libraries"),
            PublicBuildingType(key = "school", label = "School"),
        )
    ),
    PublicBuildingSector(
        key = "religious",
        label = "Religious",
        buildings = listOf(
            PublicBuildingType(key = "cemetery", label = "Cemetery"),
            PublicBuildingType(key = "mosque", label = "Mosque"),
        )
    ),
    PublicBuildingSector(
        key = "tourism",
        label = "Tourism",
        buildings = listOf(
            PublicBuildingType(key = "hostel", label = "Hostel"),
            PublicBuildingType(key = "hotel", label = "Hotel"),
            PublicBuildingType(key = "motel", label = "Motel"),
        )
    ),
    PublicBuildingSector(
        key = "transport",
        label = "Transport",
        buildings = listOf(
            PublicBuildingType(key = "airport", label = "Airport"),
            PublicBuildingType(key = "bus_station", label = "Bus Station"),
            PublicBuildingType(key = "train_station", label = "Train Station"),
        )
    ),
    PublicBuildingSector(
        key = "vocational_training",
        label = "Vocational Training and Education",
        buildings = listOf(
            PublicBuildingType(key = "specialized_vocational_institute", label = "National Specialized Vocational Training Institute"),
            PublicBuildingType(key = "vocational_education_institute", label = "Vocational Education Institute"),
            PublicBuildingType(key = "vocational_apprenticeship_center", label = "Vocational Training and Apprenticeship Center"),
            PublicBuildingType(key = "vocational_training_institute", label = "Vocational Training Institute"),
        )
    ),
    PublicBuildingSector(
        key = "youth_sports",
        label = "Youth & Sports",
        buildings = listOf(
            PublicBuildingType(key = "indoor_arena", label = "Indoor Arena"),
            PublicBuildingType(key = "leisure_center", label = "Leisure Center"),
            PublicBuildingType(key = "sports_complex", label = "Sports Complex"),
            PublicBuildingType(key = "stadium", label = "Stadium"),
            PublicBuildingType(key = "swimming_pool", label = "Swimming Pool"),
            PublicBuildingType(key = "youth_clubs", label = "Youth Clubs"),
            PublicBuildingType(key = "youth_hostel", label = "Youth Hostel"),
        )
    ),
)
