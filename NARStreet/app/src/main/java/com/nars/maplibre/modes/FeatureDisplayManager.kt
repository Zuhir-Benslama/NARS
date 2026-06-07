package com.nars.maplibre.modes

import com.geoman.maplibre.geoman.Geoman
import com.geoman.maplibre.geoman.core.GeomanCoreConstants
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.utils.NarsLogger
import org.maplibre.android.maps.MapLibreMap

class FeatureDisplayManager(
    private val geoman: Geoman,
    private val featureRenderer: FeatureRenderer,
    private val geometryConverter: GeometryConverter,
    private val map: MapLibreMap?
) {
    private val displayedFeatureIds = mutableSetOf<String>()
    var currentPhase: PhaseDefinition? = null

    fun addFeature(feature: NarsFeature) {
        displayedFeatureIds.add(feature.id)
        featureRenderer.addFeature(feature)
        val geoJsonFeature = geometryConverter.convertToGeoJson(feature)
        geoman.addGeoJsonFeature(geoJsonFeature, geometryConverter.getSourceNameForGeometry(feature.geometry))
    }

    fun addFeatures(features: List<NarsFeature>) {
        val currentPhaseKey = currentPhase?.key
        val filtered = if (currentPhaseKey != null) {
            features.filter { it.properties.phase == currentPhaseKey }
        } else features
        filtered.forEach { addFeature(it) }
    }

    fun updateDisplayedFeatures(allFeatures: List<NarsFeature>) {
        val currentPhaseKey = currentPhase?.key
        val filtered = if (currentPhaseKey != null) {
            allFeatures.filter { it.properties.phase == currentPhaseKey }
        } else allFeatures
        val newIds = filtered.map { it.id }.toSet()

        val toRemove = displayedFeatureIds - newIds
        toRemove.forEach { removeFeature(it) }

        val toAdd = filtered.filter { it.id !in displayedFeatureIds }
        toAdd.forEach { addFeature(it) }

        displayedFeatureIds.clear()
        displayedFeatureIds.addAll(newIds)

        if (currentPhaseKey == Phases.ROADS_KEY) {
            featureRenderer.labelAndMarkerManager.addRoadEndpointMarkers(allFeatures)
        }
    }

    fun updateFeatureId(oldId: String, newId: String) {
        if (oldId == newId) return
        displayedFeatureIds.remove(oldId)
        displayedFeatureIds.add(newId)
        featureRenderer.removeFromTracking(oldId)
    }

    fun updateFeatureOnMap(feature: NarsFeature) {
        val sourceName = "nars_${feature.id}"
        val source = map?.style?.getSource(sourceName)
        if (source is org.maplibre.android.style.sources.GeoJsonSource) {
            val geoJsonFeature = geometryConverter.convertToGeoJson(feature)
            val geoJsonString = buildGeoJsonString(geoJsonFeature)
            source.setGeoJson(geoJsonString)
            NarsLogger.d("FeatureDisplayManager", "Updated feature ${feature.id} in-place")
        } else {
            removeFeature(feature.id)
            addFeature(feature)
        }
    }

    @Suppress("TooGenericExceptionCaught")
    fun removeFeature(featureId: String) {
        displayedFeatureIds.remove(featureId)
        val geomanSourceNames = listOf(
            GeomanCoreConstants.SOURCE_MARKERS, GeomanCoreConstants.SOURCE_LINES,
            GeomanCoreConstants.SOURCE_POLYGONS, GeomanCoreConstants.SOURCE_CIRCLES
        )
        for (sourceName in geomanSourceNames) {
            val featureData = geoman.features.getFeature(sourceName, featureId)
            if (featureData != null) {
                geoman.features.removeFeature(sourceName, featureId)
                break
            }
        }

        val layerName = "nars_layer_$featureId"
        val layerNames = listOf(
            layerName, "${layerName}_outline",
            "${layerName}_stroke", "${layerName}_label"
        )
        for (name in layerNames) {
            try { map?.style?.getLayer(name)?.let { map?.style?.removeLayer(it) } }
            catch (e: Exception) { NarsLogger.w("FeatureDisplayManager", "Failed to remove layer $name: ${e.message}") }
        }
        val mapSourceNames = listOf("nars_${featureId}_edges", "nars_$featureId")
        for (name in mapSourceNames) {
            try { map?.style?.removeSource(name) }
            catch (e: Exception) {
                NarsLogger.w("FeatureDisplayManager", "Failed to remove source $name: ${e.message}")
            }
        }

        featureRenderer.labelAndMarkerManager.removeVertexMarkers(featureId)
        featureRenderer.removeFromTracking(featureId)
        NarsLogger.d("FeatureDisplayManager", "Removed feature $featureId")
    }

    fun clearAllFeatures() {
        displayedFeatureIds.clear()
        geoman.clearAllFeatures()
        featureRenderer.clearTracking()
    }

    private fun buildGeoJsonString(feature: com.geoman.maplibre.geoman.types.geojson.Feature): String {
        val geometryJson = geometryConverter.geometryToJson(feature.geometry)
        val id = feature.id ?: ""
        return """{"type":"Feature","id":"$id","geometry":$geometryJson}"""
    }
}
