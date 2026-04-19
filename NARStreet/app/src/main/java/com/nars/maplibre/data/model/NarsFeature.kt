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
    val dbId: Long? = null,
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
    val roadDbId: Long? = null,
    val roadLabel: String? = null,
    val side: String? = null, // "left" or "right"
    val entranceNumber: Int? = null,
    val mainEntranceDbId: Long? = null,
    val mainEntranceLabel: String? = null,
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
 * The 8 phases of NARS matching the web version (phases.ts: PHASES)
 * 
 * Note: Labels and hints are i18n keys. In the web version, these are
 * translated using vue-i18n. For Android, implement similar i18n support.
 */
object Phases {
    val ALL = listOf(
        PhaseDefinition(
            index = 0,
            key = "areas",
            label = "phase_areas_label",
            drawType = DrawType.POLYGON,
            color = "#8e44ad",
            hint = "phase_areas_hint"
        ),
        PhaseDefinition(
            index = 1,
            key = "districts",
            label = "phase_districts_label",
            drawType = DrawType.POLYGON,
            color = "#f39c12",
            hint = "phase_districts_hint"
        ),
        PhaseDefinition(
            index = 2,
            key = "cityCenter",
            label = "phase_cityCenter_label",
            drawType = DrawType.CIRCLE,
            color = "#e74c3c",
            hint = "phase_cityCenter_hint"
        ),
        PhaseDefinition(
            index = 3,
            key = "roads",
            label = "phase_roads_label",
            drawType = DrawType.POLYLINE,
            color = "#3498db",
            hint = "phase_roads_hint"
        ),
        PhaseDefinition(
            index = 4,
            key = "houseEntrances",
            label = "phase_houseEntrances_label",
            drawType = DrawType.MARKER,
            color = "#27ae60",
            hint = "phase_houseEntrances_hint"
        ),
        PhaseDefinition(
            index = 5,
            key = "publicBuildings",
            label = "phase_publicBuildings_label",
            drawType = DrawType.POLYGON,
            color = "#e67e22",
            hint = "phase_publicBuildings_hint"
        ),
        PhaseDefinition(
            index = 6,
            key = "publicSpaces",
            label = "phase_publicSpaces_label",
            drawType = DrawType.POLYGON,
            color = "#2ecc71",
            hint = "phase_publicSpaces_hint"
        ),
        PhaseDefinition(
            index = 7,
            key = "namingPanels",
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
        // TODO: Implement i18n lookup
        // For now, return the key - implement proper string resource lookup
        return when (phase.label) {
            "phase_areas_label" -> "Urban Areas"
            "phase_districts_label" -> "Districts"
            "phase_cityCenter_label" -> "City Center"
            "phase_roads_label" -> "Roads"
            "phase_houseEntrances_label" -> "House Entrances"
            "phase_publicBuildings_label" -> "Public Buildings"
            "phase_publicSpaces_label" -> "Public Spaces"
            "phase_namingPanels_label" -> "Naming Panels"
            else -> phase.label
        }
    }

    /** Get hint for phase (with i18n support) */
    fun getHint(phase: PhaseDefinition, context: android.content.Context): String {
        // TODO: Implement i18n lookup
        return when (phase.hint) {
            "phase_areas_hint" -> "Draw urban area boundaries"
            "phase_districts_hint" -> "Define district divisions"
            "phase_cityCenter_hint" -> "Mark the city center"
            "phase_roads_hint" -> "Draw road networks"
            "phase_houseEntrances_hint" -> "Add building entrances"
            "phase_publicBuildings_hint" -> "Map public facilities"
            "phase_publicSpaces_hint" -> "Define public areas"
            "phase_namingPanels_hint" -> "Add street naming information"
            else -> ""
        }
    }
}
