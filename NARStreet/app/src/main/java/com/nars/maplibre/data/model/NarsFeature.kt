package com.nars.maplibre.data.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

enum class NarsFeatureType(val value: String) {
    ROAD("road"),
    HOUSE_ENTRANCE("house_entrance"),
    NAMING_PANEL("naming_panel");

    companion object {
        fun fromValue(value: String): NarsFeatureType {
            return entries.find { it.value == value } ?: ROAD
        }
    }
}

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

@Serializable
sealed class Geometry {
    abstract val type: String
    abstract val coordinates: List<Double>
}

@Serializable
@SerialName("Point")
data class PointGeometry(
    override val type: String = "Point",
    override val coordinates: List<Double>
) : Geometry()

@Serializable
@SerialName("LineString")
data class LineStringGeometry(
    override val type: String = "LineString",
    override val coordinates: List<Double>
) : Geometry()

@Serializable
@SerialName("Polygon")
data class PolygonGeometry(
    override val type: String = "Polygon",
    override val coordinates: List<Double>
) : Geometry()

@Serializable
@SerialName("Circle")
data class CircleGeometry(
    override val type: String = "Circle",
    override val coordinates: List<Double>
) : Geometry()

@Serializable
sealed class FeatureProperties {
    abstract val name: String?
    abstract val phase: String
    abstract val color: String

    @Serializable
    @SerialName("roads")
    data class RoadProperties(
        override val name: String? = null,
        override val phase: String = Phases.ROADS_KEY,
        override val color: String = "#3498db",
        val roadTypeKey: String? = null,
        val roadTraffic: String? = null,
        val tradActivity: String? = null,
        val numLanes: Int? = null,
        val hasMedian: Boolean? = null,
        val hasVegetation: Boolean? = null,
        val isDeadEnd: Boolean? = null,
        val hasSidewalk: Boolean? = null
    ) : FeatureProperties()

    @Serializable
    @SerialName("houseEntrances")
    data class HouseEntranceProperties(
        override val name: String? = null,
        override val phase: String = Phases.HOUSE_ENTRANCES_KEY,
        override val color: String = "#27ae60",
        val entranceTypeKey: String? = null,
        val roadDbId: String? = null,
        val side: String? = null,
        val hasEntrance: Boolean? = null,
        val hasNumberingPanel: Boolean? = null,
        val numberingPanelCorrect: Boolean? = null,
        val numberingPanelPositionCorrect: Boolean? = null
    ) : FeatureProperties()

    @Serializable
    @SerialName("namingPanels")
    data class NamingPanelProperties(
        override val name: String? = null,
        override val phase: String = Phases.NAMING_PANELS_KEY,
        override val color: String = "#9b59b6",
        val hasNamingPanelLocation: Boolean? = null,
        val hasNamingPanel: Boolean? = null,
        val namingCorrect: Boolean? = null,
        val namingPanelPositionCorrect: Boolean? = null
    ) : FeatureProperties()
}

data class PhaseDefinition(
    val index: Int,
    val key: String,
    val label: String,
    val drawType: DrawType,
    val color: String,
    val hint: String
) {
    val parsedColor: androidx.compose.ui.graphics.Color by lazy {
        androidx.compose.ui.graphics.Color(android.graphics.Color.parseColor(color))
    }
}

enum class DrawType {
    POLYGON,
    POLYLINE,
    CIRCLE,
    MARKER
}

object Phases {
    const val ROADS_KEY = "roads"
    const val HOUSE_ENTRANCES_KEY = "houseEntrances"
    const val NAMING_PANELS_KEY = "namingPanels"

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

    fun getByKey(key: String): PhaseDefinition? = ALL.find { it.key == key }

    fun getIndexByKey(key: String): Int = ALL.indexOfFirst { it.key == key }

    fun getByIndex(index: Int): PhaseDefinition? = ALL.getOrNull(index)

    fun getDisplayLabel(phase: PhaseDefinition, context: android.content.Context): String {
        return when (phase.key) {
            ROADS_KEY -> context.getString(com.nars.maplibre.R.string.phase_roads)
            HOUSE_ENTRANCES_KEY -> context.getString(com.nars.maplibre.R.string.phase_house_entrances)
            NAMING_PANELS_KEY -> context.getString(com.nars.maplibre.R.string.phase_naming_panels)
            else -> phase.label
        }
    }

    fun getHint(phase: PhaseDefinition, context: android.content.Context): String {
        return when (phase.key) {
            ROADS_KEY -> context.getString(com.nars.maplibre.R.string.phase_roads_hint)
            HOUSE_ENTRANCES_KEY -> context.getString(com.nars.maplibre.R.string.phase_house_entrances_hint)
            NAMING_PANELS_KEY -> context.getString(com.nars.maplibre.R.string.phase_naming_panels_hint)
            else -> ""
        }
    }
}
