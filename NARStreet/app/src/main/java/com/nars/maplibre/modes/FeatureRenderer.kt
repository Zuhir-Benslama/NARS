package com.nars.maplibre.modes

import android.graphics.Color
import com.geoman.maplibre.geoman.types.geojson.Feature
import com.nars.maplibre.data.api.escapeJson
import com.nars.maplibre.data.model.*
import com.nars.maplibre.utils.NarsLogger
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.style.layers.FillLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource

class FeatureRenderer(
    private val map: MapLibreMap
) {
    lateinit var labelAndMarkerManager: LabelAndMarkerManager

    companion object {
        private const val TAG = "FeatureRenderer"
        private const val DEFAULT_MARKER_ICON_SIZE = 0.5f
        private const val DEFAULT_CIRCLE_RADIUS_METERS = 50.0
        private const val CIRCLE_FILL_OPACITY = 0f

        const val STYLE_LINE_WIDTH_THIN = 2
        const val STYLE_LINE_WIDTH_MEDIUM = 3
        const val STYLE_LINE_WIDTH_THICK = 8
        const val STYLE_FILL_OPACITY_NONE = 0.0
        const val STYLE_FILL_OPACITY_LIGHT = 0.20
        const val STYLE_FILL_OPACITY_MEDIUM = 0.25
        const val STYLE_FILL_OPACITY_DEFAULT = 0.3
    }

    private val addedFeatureIds = mutableSetOf<String>()

    fun addFeature(feature: NarsFeature) {
        if (addedFeatureIds.contains(feature.id)) {
            NarsLogger.w(TAG, "Feature ${feature.id} already exists, skipping")
            return
        }

        val geoJsonFeature = GeometryConverter().convertToGeoJson(feature)
        val sourceName = "nars_${feature.id}"
        val layerName = "nars_layer_${feature.id}"
        val geoJsonString = buildGeoJsonString(geoJsonFeature)

        removeExistingSource(sourceName)

        map.style?.addSource(GeoJsonSource(sourceName, geoJsonString))

        val style = getFeatureStyle(feature.properties.phase)
        when (feature.geometry) {
            is PointGeometry -> addPointLayer(layerName, sourceName)
            is LineStringGeometry -> addLineLayer(layerName, sourceName, style)
            is PolygonGeometry -> addPolygonLayer(layerName, sourceName, style, feature.geometry)
            is CircleGeometry -> addCircleLayer(layerName, sourceName, style, feature.geometry)
        }
        labelAndMarkerManager.addLabelLayer(layerName, sourceName, feature.properties.name)

        addedFeatureIds.add(feature.id)
    }

    private fun addPointLayer(layerName: String, sourceName: String) {
        SymbolLayer(layerName, sourceName).apply {
            setProperties(
                org.maplibre.android.style.layers.PropertyFactory.iconImage("default-marker"),
                org.maplibre.android.style.layers.PropertyFactory.iconSize(DEFAULT_MARKER_ICON_SIZE),
                org.maplibre.android.style.layers.PropertyFactory.iconAllowOverlap(true)
            )
            map.style?.addLayer(this)
        }
    }

    private fun addLineLayer(layerName: String, sourceName: String, style: FeatureStyle) {
        LineLayer(layerName, sourceName).apply {
            setProperties(
                org.maplibre.android.style.layers.PropertyFactory.lineColor(parseColor(style.lineColor)),
                org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
            )
            map.style?.addLayer(this)
        }
    }

    private fun addPolygonLayer(layerName: String, sourceName: String, style: FeatureStyle, geom: PolygonGeometry) {
        val edgeSourceName = "${sourceName}_edges"
        removeExistingSource(edgeSourceName)
        map.style?.addSource(GeoJsonSource(edgeSourceName, GeometryConverter().buildPolygonEdgesGeoJson(geom.coordinates)))

        LineLayer("${layerName}_outline", edgeSourceName).apply {
            setProperties(
                org.maplibre.android.style.layers.PropertyFactory.lineColor(parseColor(style.lineColor)),
                org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
            )
            map.style?.addLayer(this)
        }
    }

    private fun addCircleLayer(layerName: String, sourceName: String, style: FeatureStyle, geom: CircleGeometry) {
        val radiusMeters = geom.coordinates[2].takeIf { it > 0 } ?: DEFAULT_CIRCLE_RADIUS_METERS
        val circleGeoJson = GeometryConverter().buildCircleGeoJson(geom.coordinates[0], geom.coordinates[1], radiusMeters)

        removeExistingSource(sourceName)
        map.style?.addSource(GeoJsonSource(sourceName, circleGeoJson))

        FillLayer(layerName, sourceName).apply {
            setProperties(org.maplibre.android.style.layers.PropertyFactory.fillOpacity(CIRCLE_FILL_OPACITY))
            map.style?.addLayer(this)
        }

        LineLayer("${layerName}_stroke", sourceName).apply {
            setProperties(
                org.maplibre.android.style.layers.PropertyFactory.lineColor(parseColor(style.lineColor)),
                org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
            )
            map.style?.addLayer(this)
        }
    }

    private fun removeExistingSource(sourceName: String) {
        try {
            map.style?.getSource(sourceName)?.let { map.style?.removeSource(sourceName) }
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Error removing source $sourceName: ${e.message}")
        }
    }

    private fun getFeatureStyle(phaseKey: String): FeatureStyle = when (phaseKey) {
        Phases.ROADS_KEY -> FeatureStyle("#3498db", STYLE_LINE_WIDTH_THICK)
        Phases.HOUSE_ENTRANCES_KEY -> FeatureStyle("#27ae60", STYLE_LINE_WIDTH_THIN)
        Phases.NAMING_PANELS_KEY -> FeatureStyle("#9b59b6", STYLE_LINE_WIDTH_THIN)
        else -> FeatureStyle("#8e44ad", STYLE_LINE_WIDTH_THIN)
    }

    data class FeatureStyle(
        val lineColor: String,
        val lineWidth: Int
    )

    private fun parseColor(colorStr: String): Int = try {
        Color.parseColor(if (colorStr.startsWith("#")) colorStr else "#$colorStr")
    } catch (e: Exception) {
        Color.GRAY
    }

    private fun buildGeoJsonString(feature: com.geoman.maplibre.geoman.types.geojson.Feature): String {
        val geometry = feature.geometry
        val props = feature.properties
        val escapedId = escapeJson(feature.id ?: "")
        return """{"type":"Feature","id":"$escapedId","geometry":${GeometryConverter().geometryToJson(geometry)},"properties":${propertiesToJson(props)}}"""
    }

    private fun propertiesToJson(properties: Map<String, Any?>): String {
        val props = properties.toMutableMap()
        if (props.containsKey("name") && !props.containsKey("label")) props["label"] = props["name"]
        return props.entries.joinToString(",", "{", "}") { (key, value) ->
            """ "${escapeJson(key)}": "${escapeJson(value?.toString() ?: "")}" """.trim()
        }
    }

    fun isFeatureAdded(featureId: String): Boolean = addedFeatureIds.contains(featureId)
    fun removeFromTracking(featureId: String) = addedFeatureIds.remove(featureId)
    fun clearTracking() = addedFeatureIds.clear()
    fun getTrackedCount(): Int = addedFeatureIds.size
}
