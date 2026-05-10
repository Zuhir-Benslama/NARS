package com.nars.maplibre.data.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * NARS Feature types matching the web version
 */
enum class NarsFeatureType(val value: String) {
    URBAN_AREA("urban_area"),
    DISTRICT("district"),
    CITY_CENTER("city_center"),
    ROAD("road"),
    HOUSE_ENTRANCE("house_entrance"),
    PUBLIC_BUILDING("public_building"),
    PUBLIC_SPACE("public_space"),
    NAMING_PANEL("naming_panel");

    companion object {
        fun fromValue(value: String): NarsFeatureType {
            return entries.find { it.value == value } ?: URBAN_AREA
        }
    }
}

/**
 * Feature sub-types for house entrances
 */
enum class EntranceType(val value: String, val displayName: String) {
    MAIN("main", "Main Entrance"),
    SECONDARY("secondary", "Secondary Entrance")
}

/**
 * Feature sub-types for public buildings (simplified for backward compatibility)
 * For complete hierarchy, use PUBLIC_BUILDING_SECTORS from FeatureTypes.kt
 */
enum class BuildingType(val value: String, val displayName: String) {
    EDUCATION("education", "Education"),
    HEALTH("health", "Health"),
    CULTURE("culture", "Culture"),
    SPORT("sport", "Sport"),
    ADMINISTRATION("administration", "Administration"),
    OTHER("other", "Other")
}

/**
 * Base feature data class
 */
@Serializable
data class NarsFeature(
    val id: String,
    val dbId: String? = null,
    val type: NarsFeatureType,
    val geometry: Geometry,
    val properties: FeatureProperties,
    val createdAt: Long = System.currentTimeMillis(),
    val updatedAt: Long = System.currentTimeMillis()
)

/**
 * Geometry types
 */
@Serializable
sealed class Geometry {
    abstract val type: String
    abstract val coordinates: List<Double>
}

@Serializable
@SerialName("Point")
data class PointGeometry(
    override val type: String = "Point",
    override val coordinates: List<Double> // [longitude, latitude]
) : Geometry()

@Serializable
@SerialName("LineString")
data class LineStringGeometry(
    override val type: String = "LineString",
    override val coordinates: List<Double> // Flattened [lon1, lat1, lon2, lat2, ...]
) : Geometry()

@Serializable
@SerialName("Polygon")
data class PolygonGeometry(
    override val type: String = "Polygon",
    override val coordinates: List<Double> // Flattened exterior ring
) : Geometry()

@Serializable
@SerialName("Circle")
data class CircleGeometry(
    override val type: String = "Circle",
    override val coordinates: List<Double> // [longitude, latitude, radius_meters]
) : Geometry()

/**
 * Feature properties matching web version FeatureData
 */
@Serializable
data class FeatureProperties(
    val name: String? = null,
    val number: String? = null,
    val bisNumber: Int? = null,
    val entranceType: EntranceType? = null,
    val buildingType: BuildingType? = null,
    val phase: String,
    val color: String,
    val description: String? = null,
    // Decision fields (required for most features)
    val decisionNumber: String? = null,
    val decisionDate: String? = null,
    // Phase-specific type keys
    val areaTypeKey: String? = null,
    val districtTypeKey: String? = null,
    val roadTypeKey: String? = null,
    val spaceTypeKey: String? = null,
    val sectorKey: String? = null,
    val buildingTypeKey: String? = null,
    // House entrance specific fields
    val entranceTypeKey: String? = null,
    val roadDbId: String? = null,
    val roadLabel: String? = null,
    val side: String? = null, // "left" or "right"
    val entranceNumber: Int? = null,
    val mainEntranceDbId: String? = null,
    val mainEntranceLabel: String? = null,
    // --- Roads Phase Fields (Field Mode) ---
    val roadTraffic: String? = null, // "high" | "medium" | "low"
    val tradActivity: String? = null, // "high" | "medium" | "low"
    val numLanes: Int? = null,
    val hasMedian: Boolean? = null,
    val hasVegetation: Boolean? = null,
    val isDeadEnd: Boolean? = null,
    val hasSidewalk: Boolean? = null,
    // --- HouseEntrances Phase Fields (Field Mode) ---
    val hasEntrance: Boolean? = null,
    val hasNumberingPanel: Boolean? = null,
    val numberingPanelCorrect: Boolean? = null,
    val numberingPanelPositionCorrect: Boolean? = null,
    // --- NamingPanels Phase Fields (Field Mode) ---
    val hasNamingPanelLocation: Boolean? = null,
    val hasNamingPanel: Boolean? = null,
    val namingCorrect: Boolean? = null,
    val namingPanelPositionCorrect: Boolean? = null,
    // Road endpoint marker type ("start" | "end") for roads phase
    val markerType: String? = null,
    // Additional data for extensibility
    val additionalData: Map<String, String> = emptyMap()
)

/**
 * Phase definition matching web version (types.ts: Phase)
 */
data class PhaseDefinition(
    val index: Int,
    val key: String,
    val label: String,           // i18n key (e.g., "phase_areas_label")
    val drawType: DrawType,
    val color: String,
    val hint: String             // i18n hint key (e.g., "phase_areas_hint")
)

enum class DrawType {
    POLYGON,
    POLYLINE,
    CIRCLE,
    MARKER
}

/**
 * The 3 phases of NARStreet Field Mode
 * 
 * Only Roads, HouseEntrances, and NamingPanels are available for field validation.
 * Labels are i18n keys - implement proper string resource lookup in production.
 */
object Phases {
    // Phase key constants - Full set (some may not be used in NARStreet Field Mode)
    const val AREAS_KEY = "areas"
    const val DISTRICTS_KEY = "districts"
    const val CITY_CENTER_KEY = "cityCenter"
    const val ROADS_KEY = "roads"
    const val HOUSE_ENTRANCES_KEY = "houseEntrances"
    const val PUBLIC_BUILDINGS_KEY = "publicBuildings"
    const val PUBLIC_SPACES_KEY = "publicSpaces"
    const val NAMING_PANELS_KEY = "namingPanels"

    // Active phases for NARStreet Field Mode
    val ALL = listOf(
        PhaseDefinition(
            index = 0,
            key = ROADS_KEY,
            label = "phase_roads_label",
            drawType = DrawType.POLYLINE,
            color = "#3498db",
            hint = "phase_roads_hint"
        ),
        PhaseDefinition(
            index = 1,
            key = HOUSE_ENTRANCES_KEY,
            label = "phase_houseEntrances_label",
            drawType = DrawType.MARKER,
            color = "#27ae60",
            hint = "phase_houseEntrances_hint"
        ),
        PhaseDefinition(
            index = 2,
            key = NAMING_PANELS_KEY,
            label = "phase_namingPanels_label",
            drawType = DrawType.MARKER,
            color = "#9b59b6",
            hint = "phase_namingPanels_hint"
        )
    )

    /** Get phase by key */
    fun getByKey(key: String): PhaseDefinition? = ALL.find { it.key == key }

    /** Get phase index by key */
    fun getIndexByKey(key: String): Int = ALL.indexOfFirst { it.key == key }

    /** Get phase by index */
    fun getByIndex(index: Int): PhaseDefinition? = ALL.getOrNull(index)

    /** Get display label for phase (with i18n support) */
    fun getDisplayLabel(phase: PhaseDefinition, context: android.content.Context): String {
        return when (phase.key) {
            ROADS_KEY -> context.getString(com.nars.maplibre.R.string.phase_roads)
            HOUSE_ENTRANCES_KEY -> context.getString(com.nars.maplibre.R.string.phase_house_entrances)
            NAMING_PANELS_KEY -> context.getString(com.nars.maplibre.R.string.phase_naming_panels)
            else -> phase.label
        }
    }

    /** Get hint for phase (with i18n support) */
    fun getHint(phase: PhaseDefinition, context: android.content.Context): String {
        return when (phase.key) {
            ROADS_KEY -> context.getString(com.nars.maplibre.R.string.phase_roads_hint)
            HOUSE_ENTRANCES_KEY -> context.getString(com.nars.maplibre.R.string.phase_house_entrances_hint)
            NAMING_PANELS_KEY -> context.getString(com.nars.maplibre.R.string.phase_naming_panels_hint)
            else -> ""
        }
    }
}
