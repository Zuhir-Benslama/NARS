package com.nars.maplibre.modes

import android.graphics.Color
import com.nars.maplibre.data.api.escapeJson
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import com.nars.maplibre.utils.NarsLogger
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.style.expressions.Expression
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource

class LabelAndMarkerManager(
    private val map: MapLibreMap
) {
    companion object {
        private const val TAG = "LabelAndMarkerManager"
        private const val LABEL_TEXT_SIZE = 14f
        private const val LABEL_HALO_WIDTH = 2f
    }

    private val vertexMarkerIds = mutableSetOf<String>()

    fun addLabelLayer(layerName: String, sourceName: String, labelText: String?) {
        if (labelText.isNullOrBlank()) return
        val labelLayerName = "${layerName}_label"
        try {
            map.style?.getLayer(labelLayerName)?.let { map.style?.removeLayer(labelLayerName) }
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Failed to remove existing label layer: ${e.message}")
        }

        val labelLayer = SymbolLayer(labelLayerName, sourceName).apply {
            setProperties(
                org.maplibre.android.style.layers.PropertyFactory.textField(Expression.literal(labelText)),
                org.maplibre.android.style.layers.PropertyFactory.textColor(Color.BLACK),
                org.maplibre.android.style.layers.PropertyFactory.textSize(LABEL_TEXT_SIZE),
                org.maplibre.android.style.layers.PropertyFactory.textFont(arrayOf("Noto Sans Regular")),
                org.maplibre.android.style.layers.PropertyFactory.textAllowOverlap(true),
                org.maplibre.android.style.layers.PropertyFactory.textIgnorePlacement(true),
                org.maplibre.android.style.layers.PropertyFactory.textHaloColor(Color.WHITE),
                org.maplibre.android.style.layers.PropertyFactory.textHaloWidth(LABEL_HALO_WIDTH)
            )
        }
        try {
            map.style?.addLayer(labelLayer)
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Error adding label: ${e.message}")
        }
    }

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
    }

    private fun addEndpointMarker(id: String, lon: Double, lat: Double, isStart: Boolean) {
        val layerName = "nars_$id"
        val sourceName = "${layerName}_src"
        val geoJson = """{"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [$lon, $lat]}, "properties": {}}]}"""

        try {
            map.style?.addSource(GeoJsonSource(sourceName, geoJson))
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Failed to add endpoint marker source: ${e.message}")
            return
        }

        val color = if (isStart) Color.parseColor("#2ecc71") else Color.parseColor("#e74c3c")
        val circleLayer = CircleLayer("${layerName}_circle", sourceName).apply {
            setProperties(
                org.maplibre.android.style.layers.PropertyFactory.circleColor(color),
                org.maplibre.android.style.layers.PropertyFactory.circleRadius(14f),
                org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor(Color.WHITE),
                org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(3f)
            )
        }
        try { map.style?.addLayer(circleLayer) } catch (e: Exception) {
            NarsLogger.w(TAG, "Failed to add circle layer: ${e.message}")
        }
    }

    private fun addLabelAt(layerName: String, labelText: String?, lon: Double, lat: Double) {
        if (labelText.isNullOrBlank()) return
        val sourceName = "${layerName}_src"
        val escapedText = escapeJson(labelText)
        val geoJson = """{"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [$lon, $lat]}, "properties": {"text": "$escapedText"}}]}"""

        try { map.style?.addSource(GeoJsonSource(sourceName, geoJson)) } catch (e: Exception) {
            NarsLogger.w(TAG, "Failed to add label source: ${e.message}")
        }

        val symbolLayer = SymbolLayer(layerName, sourceName).apply {
            setProperties(
                org.maplibre.android.style.layers.PropertyFactory.textField(Expression.get("text")),
                org.maplibre.android.style.layers.PropertyFactory.textColor(Color.BLACK),
                org.maplibre.android.style.layers.PropertyFactory.textSize(14f),
                org.maplibre.android.style.layers.PropertyFactory.textFont(arrayOf("Noto Sans Regular")),
                org.maplibre.android.style.layers.PropertyFactory.textAllowOverlap(true),
                org.maplibre.android.style.layers.PropertyFactory.textHaloColor(Color.WHITE),
                org.maplibre.android.style.layers.PropertyFactory.textHaloWidth(3f)
            )
        }
        try { map.style?.addLayer(symbolLayer) } catch (e: Exception) {
            NarsLogger.w(TAG, "Failed to add label layer: ${e.message}")
        }
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
                val geoJson = """{"type": "Feature", "geometry": {"type": "Point", "coordinates": [${coord[0]}, ${coord[1]}]}, "properties": {"isVertex": true}}"""
                map.style?.addSource(GeoJsonSource(vertexSourceName, geoJson))

                val circleLayer = CircleLayer(vertexLayerName, vertexSourceName).apply {
                    setProperties(
                        org.maplibre.android.style.layers.PropertyFactory.circleColor(Color.RED),
                        org.maplibre.android.style.layers.PropertyFactory.circleRadius(6f),
                        org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor(Color.WHITE),
                        org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth(2f)
                    )
                }
                map.style?.addLayer(circleLayer)
                vertexMarkerIds.add(vertexLayerName)
            } catch (e: Exception) {
                NarsLogger.w(TAG, "Error adding vertex marker: ${e.message}")
            }
        }
    }

    fun removeVertexMarkers(featureId: String) {
        val prefix = "nars_vertex_layer_${featureId}_"
        val markersToRemove = vertexMarkerIds.filter { it.startsWith(prefix) }
        for (markerId in markersToRemove) {
            val sourceName = markerId.replace("nars_vertex_layer_", "nars_vertex_")
            try {
                map.style?.getLayer(markerId)?.let {
                    map.style?.removeLayer(it)
                    map.style?.removeSource(sourceName)
                }
                vertexMarkerIds.remove(markerId)
            } catch (e: Exception) {
                NarsLogger.w(TAG, "Failed to remove vertex marker $markerId: ${e.message}")
            }
        }
    }
}
