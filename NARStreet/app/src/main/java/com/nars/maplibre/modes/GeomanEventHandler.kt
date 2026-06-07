package com.nars.maplibre.modes

import com.geoman.maplibre.geoman.types.events.GmDrawEvent
import com.geoman.maplibre.geoman.types.events.GmEditEvent
import com.geoman.maplibre.geoman.types.events.GmMapEvent
import com.nars.maplibre.data.model.CircleGeometry
import com.nars.maplibre.data.model.Geometry
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import com.nars.maplibre.utils.NarsLogger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import com.geoman.maplibre.geoman.core.features.FeatureData
import com.geoman.maplibre.geoman.types.geojson.LngLat
import com.geoman.maplibre.geoman.types.geojson.LineString
import com.geoman.maplibre.geoman.types.geojson.MultiPolygon
import com.geoman.maplibre.geoman.types.geojson.Point
import com.geoman.maplibre.geoman.types.geojson.Polygon

class GeomanEventHandler(
    private val scope: CoroutineScope,
    private val geoman: com.geoman.maplibre.geoman.Geoman,
    private val onFeatureCreated: (NarsFeature) -> Unit,
    private val onFeatureUpdated: (NarsFeature) -> Unit,
    private val onFeatureDeleted: (String) -> Unit
) {
    companion object {
        private const val TAG = "GeomanEventHandler"
    }

    private var currentPhase: PhaseDefinition? = null
    private var editingFeatureId: String? = null
    private var editingFeature: NarsFeature? = null

    fun setCurrentPhase(phase: PhaseDefinition) {
        currentPhase = phase
    }

    fun setEditingFeature(id: String?, feature: NarsFeature?) {
        editingFeatureId = id
        editingFeature = feature
    }

    fun getEditingFeatureId(): String? = editingFeatureId
    fun getEditingFeature(): NarsFeature? = editingFeature

    fun setupEventListeners() {
        scope.launch {
            geoman.events.events.collect { event ->
                when (event) {
                    is GmMapEvent.Loaded -> {
                        NarsLogger.d(TAG, "Geoman loaded")
                    }
                    is GmDrawEvent.Create -> {
                        NarsLogger.d(TAG, "GmDrawEvent.Create received: shape=${event.shape}")
                        handleFeatureCreated(event.feature as? FeatureData)
                    }
                    is GmDrawEvent.EditEnd -> {
                        NarsLogger.d(TAG, "Edit ended: ${event.shape}")
                        handleEditEnd()
                    }
                    is GmEditEvent.ChangeEnd -> {
                        NarsLogger.d(TAG, "Geometry changed")
                        handleGeometryChanged(event.feature as? FeatureData)
                    }
                    is GmEditEvent.Delete -> {
                        NarsLogger.d(TAG, "Feature deleted")
                        handleDeleted()
                    }
                }
            }
        }
    }

    internal fun handleFeatureCreated(featureData: FeatureData?) {
        val phase = currentPhase ?: run {
            NarsLogger.e(TAG, "No current phase set when creating feature!")
            return
        }

        NarsLogger.d(TAG, "Creating feature for phase: ${phase.label} (${phase.key})")
        val narsFeature = createNarsFeatureFromFeatureData(featureData, phase)
        onFeatureCreated(narsFeature)
    }

    internal fun createNarsFeatureFromFeatureData(featureData: FeatureData?, phase: PhaseDefinition): NarsFeature {
        val geometry = if (featureData != null) {
            if (featureData.properties["shapeType"] == "circle" ||
                featureData.properties["radius"] != null) {
                extractCircleGeometry(featureData)
            } else {
                extractGeometryFromGeoJson(featureData.geometry)
            }
        } else {
            extractGeometryFromGeoJson(null)
        }

        return NarsFeature(
            id = featureData?.id ?: java.util.UUID.randomUUID().toString(),
            type = getFeatureTypeFromPhase(phase),
            geometry = geometry,
            properties = when (phase.key) {
                com.nars.maplibre.data.model.Phases.ROADS_KEY ->
                    com.nars.maplibre.data.model.FeatureProperties.RoadProperties()
                com.nars.maplibre.data.model.Phases.HOUSE_ENTRANCES_KEY ->
                    com.nars.maplibre.data.model.FeatureProperties.HouseEntranceProperties()
                com.nars.maplibre.data.model.Phases.NAMING_PANELS_KEY ->
                    com.nars.maplibre.data.model.FeatureProperties.NamingPanelProperties()
                else -> com.nars.maplibre.data.model.FeatureProperties.RoadProperties()
            }
        )
    }

    internal fun handleEditEnd() {
        editingFeatureId = null
        editingFeature = null
    }

    internal fun handleGeometryChanged(featureData: FeatureData?) {
        val data = featureData ?: return
        val geometry = extractGeometryFromFeatureData(data)
        editingFeature?.let { original ->
            val updated = original.copy(geometry = geometry)
            onFeatureUpdated(updated)
        }
    }

    internal fun handleDeleted() {
        editingFeatureId?.let { featureId ->
            onFeatureDeleted(featureId)
        }
        editingFeatureId = null
        editingFeature = null
    }

    private fun extractGeometryFromGeoJson(geometry: com.geoman.maplibre.geoman.types.geojson.Geometry?): Geometry {
        if (geometry == null) {
            NarsLogger.w(TAG, "No geometry in feature")
            return PointGeometry(coordinates = listOf(0.0, 0.0))
        }

        return when (geometry) {
            is Point -> {
                val coords = geometry.coordinates
                PointGeometry(coordinates = listOf(coords[0], coords[1]))
            }
            is LineString -> {
                val flattened = geometry.coordinates.flatMap { coord -> listOf(coord[0], coord[1]) }
                LineStringGeometry(coordinates = flattened)
            }
            is Polygon -> {
                if (geometry.coordinates.isNotEmpty()) {
                    val exteriorRing = geometry.coordinates[0]
                    val flattened = exteriorRing.flatMap { coord -> listOf(coord[0], coord[1]) }
                    PolygonGeometry(coordinates = flattened)
                } else {
                    PolygonGeometry(coordinates = emptyList())
                }
            }
            is MultiPolygon -> {
                if (geometry.coordinates.isNotEmpty() && geometry.coordinates[0].isNotEmpty()) {
                    val exteriorRing = geometry.coordinates[0][0]
                    val flattened = exteriorRing.flatMap { coord -> listOf(coord[0], coord[1]) }
                    PolygonGeometry(coordinates = flattened)
                } else {
                    PolygonGeometry(coordinates = emptyList())
                }
            }
            else -> {
                NarsLogger.w(TAG, "Unknown geometry type: ${geometry::class.simpleName}")
                PointGeometry(coordinates = listOf(0.0, 0.0))
            }
        }
    }

    internal fun extractCircleGeometry(featureData: FeatureData): CircleGeometry {
        val center = featureData.properties["center"] as? LngLat
        val radius = featureData.properties["radius"] as? Double

        return if (center != null && radius != null) {
            CircleGeometry(coordinates = listOf(center.longitude, center.latitude, radius))
        } else if (featureData.geometry is Polygon) {
            val polygon = featureData.geometry as Polygon
            val ring = polygon.getExteriorRing()
            if (ring.size >= 2) {
                val sumLon = ring.sumOf { it.longitude }
                val sumLat = ring.sumOf { it.latitude }
                val avgLon = sumLon / ring.size
                val avgLat = sumLat / ring.size
                val centerPoint = LngLat(avgLon, avgLat)
                val centerLngLat = ring.first()
                val calcRadius = com.geoman.maplibre.geoman.utils.GeometryUtils.calculateDistance(
                    centerPoint, centerLngLat
                )
                CircleGeometry(coordinates = listOf(avgLon, avgLat, calcRadius))
            } else {
                CircleGeometry(coordinates = listOf(0.0, 0.0, 0.0))
            }
        } else {
            CircleGeometry(coordinates = listOf(0.0, 0.0, 0.0))
        }
    }

    fun extractGeometryFromFeatureData(featureData: FeatureData): Geometry {
        return extractGeometryFromGeoJson(featureData.geometry)
    }

    internal fun getFeatureTypeFromPhase(phase: PhaseDefinition): com.nars.maplibre.data.model.NarsFeatureType {
        return when (phase.key) {
            Phases.ROADS_KEY -> com.nars.maplibre.data.model.NarsFeatureType.ROAD
            Phases.HOUSE_ENTRANCES_KEY -> com.nars.maplibre.data.model.NarsFeatureType.HOUSE_ENTRANCE
            Phases.NAMING_PANELS_KEY -> com.nars.maplibre.data.model.NarsFeatureType.NAMING_PANEL
            else -> com.nars.maplibre.data.model.NarsFeatureType.ROAD
        }
    }
}
