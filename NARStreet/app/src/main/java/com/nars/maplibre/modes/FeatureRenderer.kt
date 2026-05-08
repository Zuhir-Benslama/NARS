package com.nars.maplibre.modes

import android.graphics.Color
import com.geoman.maplibre.geoman.GeomanConstants
import com.nars.maplibre.data.model.CircleGeometry
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import com.nars.maplibre.utils.NarsLogger
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.FillLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.android.style.expressions.Expression

/**
 * Handles rendering features on MapLibre map
 */
class FeatureRenderer(
    private val map: MapLibreMap
) {
    companion object {
        private const val TAG = "FeatureRenderer"
        
        private const val DEFAULT_MARKER_ICON_SIZE = 0.5f
        private const val DEFAULT_CIRCLE_RADIUS_METERS = 50.0
        private const val CIRCLE_FILL_OPACITY = 0f
        private const val LABEL_TEXT_SIZE = 14f
        private const val LABEL_HALO_WIDTH = 2f
        
        private const val HEX_COLOR_SHORT_LENGTH = 6
        private const val HEX_COLOR_LONG_LENGTH = 8
        
        // Phase style defaults
        private const val STYLE_LINE_WIDTH_THIN = 2
        private const val STYLE_LINE_WIDTH_MEDIUM = 3
        private const val STYLE_LINE_WIDTH_THICK = 8
        private const val STYLE_FILL_OPACITY_NONE = 0.0
        private const val STYLE_FILL_OPACITY_LIGHT = 0.20
        private const val STYLE_FILL_OPACITY_MEDIUM = 0.25
        private const val STYLE_FILL_OPACITY_DEFAULT = 0.3
        private const val STYLE_FILL_OPACITY_SEMI = 0.5
    }

    private val addedFeatureIds = mutableSetOf<String>()

    /**
     * Add a feature to the map (render directly to MapLibre)
     */
    fun addFeature(feature: NarsFeature) {
        if (addedFeatureIds.contains(feature.id)) {
            NarsLogger.w(TAG, "Feature ${feature.id} already exists, skipping")
            return
        }

        val geoJsonFeature = GeometryConverter().convertToGeoJson(feature)
        val sourceName = "nars_${feature.id}"
        val layerName = "nars_layer_${feature.id}"

        val geoJsonString = buildGeoJsonString(geoJsonFeature)

        try {
            map.style?.getSource(sourceName)?.let {
                map.style?.removeSource(sourceName)
            }
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Error checking/removing existing source: ${e.message}")
        }

        val source = GeoJsonSource(sourceName, geoJsonString)
        map.style?.addSource(source)

        val phaseKey = feature.properties.phase
        val style = getFeatureStyle(phaseKey)

        when (feature.geometry) {
            is PointGeometry -> {
                val layer = SymbolLayer(layerName, sourceName)
                layer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.iconImage("default-marker"),
                    org.maplibre.android.style.layers.PropertyFactory.iconSize(DEFAULT_MARKER_ICON_SIZE),
                    org.maplibre.android.style.layers.PropertyFactory.iconAllowOverlap(true)
                )
                map.style?.addLayer(layer)
                addLabelLayer(layerName, sourceName, feature.properties.name)
            }
            is LineStringGeometry -> {
                val lineColor = parseColor(style.lineColor)
                val layer = LineLayer(layerName, sourceName)
                layer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.lineColor(lineColor),
                    org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
                )
                map.style?.addLayer(layer)
                addLabelLayer(layerName, sourceName, feature.properties.name)
            }
            is PolygonGeometry -> {
                val lineColor = parseColor(style.lineColor)
                val edgeSourceName = "${sourceName}_edges"
                val edgeGeoJson = GeometryConverter().buildPolygonEdgesGeoJson(feature.geometry.coordinates)

                try {
                    map.style?.getSource(edgeSourceName)?.let {
                        map.style?.removeSource(edgeSourceName)
                    }
                } catch (e: Exception) {
                    NarsLogger.w(TAG, "Error checking/removing existing edge source: ${e.message}")
                }

                val edgeSource = GeoJsonSource(edgeSourceName, edgeGeoJson)
                map.style?.addSource(edgeSource)

                val outlineLayer = LineLayer("${layerName}_outline", edgeSourceName)
                outlineLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.lineColor(lineColor),
                    org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
                )
                map.style?.addLayer(outlineLayer)
                addLabelLayer(layerName, sourceName, feature.properties.name)
            }
            is CircleGeometry -> {
                val lineColor = parseColor(style.lineColor)
                val circleGeom = feature.geometry
                val radiusMeters = circleGeom.coordinates[2].takeIf { it > 0 } ?: DEFAULT_CIRCLE_RADIUS_METERS
                val centerLng = circleGeom.coordinates[0]
                val centerLat = circleGeom.coordinates[1]

                val circlePolygonGeoJson = GeometryConverter().buildCircleGeoJson(centerLng, centerLat, radiusMeters)

                try {
                    map.style?.getSource(sourceName)?.let {
                        map.style?.removeSource(sourceName)
                    }
                } catch (e: Exception) {
                    NarsLogger.w(TAG, "Error checking/removing existing circle source: ${e.message}")
                }

                val circleSource = GeoJsonSource(sourceName, circlePolygonGeoJson)
                map.style?.addSource(circleSource)

                val fillLayer = FillLayer(layerName, sourceName)
                fillLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.fillOpacity(CIRCLE_FILL_OPACITY)
                )
                map.style?.addLayer(fillLayer)

                val strokeLayer = LineLayer("${layerName}_stroke", sourceName)
                strokeLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.lineColor(lineColor),
                    org.maplibre.android.style.layers.PropertyFactory.lineWidth(style.lineWidth.toFloat())
                )
                map.style?.addLayer(strokeLayer)
                addLabelLayer(layerName, sourceName, feature.properties.name)
            }
        }

        addedFeatureIds.add(feature.id)
        NarsLogger.d(TAG, "Added feature ${feature.id} to tracking set (total: ${addedFeatureIds.size})")
    }

    /**
     * Add label layer for a feature
     */
    private fun addLabelLayer(layerName: String, sourceName: String, labelText: String?) {
        if (labelText.isNullOrBlank()) return

        val labelLayerName = "${layerName}_label"

        try {
            map.style?.getLayer(labelLayerName)?.let {
                map.style?.removeLayer(labelLayerName)
            }
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Failed to remove label layer: ${e.message}")
        }

        val labelLayer = SymbolLayer(labelLayerName, sourceName)
        val textExpr = Expression.literal(labelText)

        labelLayer.setProperties(
            org.maplibre.android.style.layers.PropertyFactory.textField(textExpr),
            org.maplibre.android.style.layers.PropertyFactory.textColor(Color.BLACK),
            org.maplibre.android.style.layers.PropertyFactory.textSize(LABEL_TEXT_SIZE),
            org.maplibre.android.style.layers.PropertyFactory.textFont(
                arrayOf("Noto Sans Regular", "Noto Sans Bold")
            ),
            org.maplibre.android.style.layers.PropertyFactory.textAllowOverlap(true),
            org.maplibre.android.style.layers.PropertyFactory.textIgnorePlacement(true),
            org.maplibre.android.style.layers.PropertyFactory.textHaloColor(Color.WHITE),
            org.maplibre.android.style.layers.PropertyFactory.textHaloWidth(LABEL_HALO_WIDTH)
        )

        try {
            map.style?.addLayer(labelLayer)
            NarsLogger.d(TAG, "Added label layer $labelLayerName: $labelText")
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Error adding label: ${e.message}")
        }
    }

    /**
     * Get feature style
     */
    private fun getFeatureStyle(phaseKey: String): FeatureStyle {
        return when (phaseKey) {
            Phases.ROADS_KEY ->
                FeatureStyle("#3498db", "#3498db", STYLE_FILL_OPACITY_NONE, STYLE_LINE_WIDTH_THICK)
            Phases.HOUSE_ENTRANCES_KEY ->
                FeatureStyle("#27ae60", "#27ae60", STYLE_FILL_OPACITY_NONE, STYLE_LINE_WIDTH_THIN)
            Phases.NAMING_PANELS_KEY ->
                FeatureStyle("#9b59b6", "#9b59b6", STYLE_FILL_OPACITY_NONE, STYLE_LINE_WIDTH_THIN)
            else ->
                FeatureStyle("#8e44ad", "#8e44ad", STYLE_FILL_OPACITY_DEFAULT, STYLE_LINE_WIDTH_THIN)
        }
    }

    /**
     * Feature style data class
     */
    data class FeatureStyle(
        val fillColor: String,
        val lineColor: String,
        val fillOpacity: Double,
        val lineWidth: Int
    )

    /**
     * Parse color string to Android Color Int
     */
    private fun parseColor(colorStr: String): Int {
        return try {
            val color = colorStr.trimStart('#')
            when (color.length) {
                HEX_COLOR_SHORT_LENGTH -> Color.parseColor("#$color")
                HEX_COLOR_LONG_LENGTH -> Color.parseColor("#$color")
                else -> Color.parseColor("#$colorStr")
            }
        } catch (e: Exception) {
            Color.GRAY
        }
    }

    /**
     * Build GeoJSON string from feature
     */
    private fun buildGeoJsonString(feature: com.geoman.maplibre.geoman.types.geojson.Feature): String {
        val geometry = feature.geometry
        val props = feature.properties

        return """
            {
                "type": "Feature",
                "id": "${feature.id}",
                "geometry": ${GeometryConverter().geometryToJson(geometry)},
                "properties": ${propertiesToJson(props)}
            }
        """.trimIndent()
    }

    /**
     * Convert properties map to JSON string
     */
    private fun propertiesToJson(properties: Map<String, Any?>): String {
        val props = properties.toMutableMap()
        if (props.containsKey("name") && !props.containsKey("label")) {
            props["label"] = props["name"]
        }
        return props.entries.joinToString(",", "{", "}") { (key, value) ->
            """ "$key": "${value ?: ""}" """.trim()
        }
    }

    fun isFeatureAdded(featureId: String): Boolean = addedFeatureIds.contains(featureId)
    fun removeFromTracking(featureId: String) = addedFeatureIds.remove(featureId)
    fun clearTracking() = addedFeatureIds.clear()
    fun getTrackedCount(): Int = addedFeatureIds.size

    fun addRoadEndpointMarkers(allFeatures: List<NarsFeature>) {
        val roads = allFeatures.filter { it.properties.phase == "roads" }
        if (roads.isEmpty()) return

        for (road in roads) {
            val geometry = road.geometry as? LineStringGeometry ?: continue
            val coords = geometry.coordinates.chunked(2)
            if (coords.size < 2) continue

            addEndpointMarker(id = "${road.id}_start", lon = coords[0][0], lat = coords[0][1], isStart = true)
            addEndpointMarker(id = "${road.id}_end", lon = coords.last()[0], lat = coords.last()[1], isStart = false)

            val midIdx = coords.size / 2
            addLabelAt(layerName = "${road.id}_label", labelText = road.properties.name, lon = coords[midIdx][0], lat = coords[midIdx][1])
        }
        NarsLogger.d(TAG, "Added road endpoint markers")
    }

    private fun addEndpointMarker(id: String, lon: Double, lat: Double, isStart: Boolean) {
        val layerName = "nars_$id"
        val sourceName = "${layerName}_src"

        val geoJson = """{"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [$lon, $lat]}, "properties": {}}]}""".trimIndent()

        try {
            val source = GeoJsonSource(sourceName, geoJson)
            map.style?.addSource(source)
        } catch (e: Exception) { return }

        val color = if (isStart) Color.parseColor("#2ecc71") else Color.parseColor("#e74c3c")

        val circleLayer = CircleLayer("${layerName}_circle", sourceName)
        circleLayer.setProperties(
            org.maplibre.android.style.layers.PropertyFactory.circleColor(color),
            org.maplibre.android.style.layers.PropertyFactory.circleRadius(14f),
            org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor(Color.WHITE),
            org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(3f)
        )

        try { map.style?.addLayer(circleLayer) } catch (e: Exception) {}
    }

    private fun addLabelAt(layerName: String, labelText: String?, lon: Double, lat: Double) {
        if (labelText.isNullOrBlank()) return

        val sourceName = "${layerName}_src"

        val geoJson = """{"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [$lon, $lat]}, "properties": {"text": "$labelText"}}]}""".trimIndent()

        try { map.style?.addSource(GeoJsonSource(sourceName, geoJson)) } catch (e: Exception) {}

        val symbolLayer = SymbolLayer(layerName, sourceName)
        symbolLayer.setProperties(
            org.maplibre.android.style.layers.PropertyFactory.textField(Expression.get("text")),
            org.maplibre.android.style.layers.PropertyFactory.textColor(Color.BLACK),
            org.maplibre.android.style.layers.PropertyFactory.textSize(14f),
            org.maplibre.android.style.layers.PropertyFactory.textFont(arrayOf("Noto Sans Regular", "Noto Sans Bold")),
            org.maplibre.android.style.layers.PropertyFactory.textAllowOverlap(true),
            org.maplibre.android.style.layers.PropertyFactory.textHaloColor(Color.WHITE),
            org.maplibre.android.style.layers.PropertyFactory.textHaloWidth(3f)
        )

        try { map.style?.addLayer(symbolLayer) } catch (e: Exception) {}
    }

    fun addVertexMarkers(feature: NarsFeature) {
        val coordinates = when (feature.geometry) {
            is LineStringGeometry -> feature.geometry.coordinates.chunked(2).map { doubleArrayOf(it[0], it[1]) }
            is PolygonGeometry -> feature.geometry.coordinates.chunked(2).map { doubleArrayOf(it[0], it[1]) }
            else -> return
        }

        coordinates.forEachIndexed { index, coord ->
            val vertexSourceName = "nars_vertex_${feature.id}_$index"
            val vertexLayerName = "nars_vertex_layer_${feature.id}_$index"

            try {
                map.style?.getSource(vertexSourceName)?.let { map.style?.removeSource(vertexSourceName) }

                val geoJson = """{"type": "Feature", "geometry": {"type": "Point", "coordinates": [${coord[0]}, ${coord[1]}]}, "properties": {"isVertex": true}}""".trimIndent()

                val source = GeoJsonSource(vertexSourceName, geoJson)
                map.style?.addSource(source)

                val circleLayer = CircleLayer(vertexLayerName, vertexSourceName)
                circleLayer.setProperties(
                    org.maplibre.android.style.layers.PropertyFactory.circleColor(Color.RED),
                    org.maplibre.android.style.layers.PropertyFactory.circleRadius(6f),
                    org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor(Color.WHITE),
                    org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(2f)
                )
                map.style?.addLayer(circleLayer)
            } catch (e: Exception) {
                NarsLogger.w(TAG, "Error adding vertex marker: ${e.message}")
            }
        }
    }

    fun removeVertexMarkers(featureId: String) {
        for (i in 0 until 100) {
            val vertexLayerName = "nars_vertex_layer_${featureId}_$i"
            val vertexSourceName = "nars_vertex_${featureId}_$i"
            try {
                map.style?.getLayer(vertexLayerName)?.let {
                    map.style?.removeLayer(it)
                    map.style?.removeSource(vertexSourceName)
                }
            } catch (e: Exception) { break }
        }
    }
}
