@file:Suppress("DEPRECATION")

package com.nars.maplibre.modes

import android.content.Context
import android.util.Log
import com.geoman.maplibre.geoman.Geoman
import com.geoman.maplibre.geoman.GeomanConstants
import com.geoman.maplibre.geoman.core.options.GmOptionsData
import com.geoman.maplibre.geoman.core.options.SettingsOptions
import com.geoman.maplibre.geoman.types.DrawModeName
import com.geoman.maplibre.geoman.types.EditModeName
import com.nars.maplibre.data.model.DrawType
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView

/**
 * NARS wrapper for Geoman — thin orchestrator that delegates to dedicated modules.
 */
class NarsGeoman(
    private val mapView: MapView,
    private val map: MapLibreMap,
    private val context: Context,
    private val onFeatureCreated: (NarsFeature) -> Unit,
    private val onFeatureUpdated: (NarsFeature) -> Unit,
    private val onFeatureDeleted: (String) -> Unit
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    val geoman: Geoman

    private val eventHandler: GeomanEventHandler
    private val featureRenderer: FeatureRenderer
    private val geometryConverter = GeometryConverter()
    private val snappingEngine = SnappingEngine()

    private var currentPhase: PhaseDefinition? = null

    // State flows
    private val _isDrawing = MutableStateFlow(false)
    val isDrawing: StateFlow<Boolean> = _isDrawing.asStateFlow()

    private val _isEditing = MutableStateFlow(false)
    val isEditing: StateFlow<Boolean> = _isEditing.asStateFlow()

    init {
        val options = GmOptionsData(
            settings = SettingsOptions(
                useControlsUi = true,
                showControlsOnMap = false,
                enableSnap = true,
                snapDistance = 20f
            )
        )
        geoman = Geoman(mapView, map, options)

        featureRenderer = FeatureRenderer(map)
        eventHandler = GeomanEventHandler(scope, geoman, onFeatureCreated, onFeatureUpdated, onFeatureDeleted)
        eventHandler.setupEventListeners()

        Log.d("NarsGeoman", "Initialized — delegating to FeatureRenderer, GeomanEventHandler, GeometryConverter, SnappingEngine")
    }

    fun setCurrentPhase(phase: PhaseDefinition) {
        currentPhase = phase
        eventHandler.setCurrentPhase(phase)
    }

    fun startDrawing() {
        val phase = currentPhase ?: return
        Log.d("NarsGeoman", "startDrawing for phase: ${phase.label}, drawType: ${phase.drawType}")

        geoman.disableAllModes()
        _isEditing.value = false
        eventHandler.setEditingFeature(null, null)
        _isDrawing.value = true

        when (phase.drawType) {
            DrawType.POLYGON -> geoman.enableDraw(DrawModeName.POLYGON)
            DrawType.POLYLINE -> geoman.enableDraw(DrawModeName.LINE)
            DrawType.CIRCLE -> geoman.enableDraw(DrawModeName.CIRCLE)
            DrawType.MARKER -> geoman.enableDraw(DrawModeName.MARKER)
        }
    }

    fun stopDrawing() {
        _isDrawing.value = false
        geoman.disableAllModes()
    }

    fun startEditing(feature: NarsFeature) {
        Log.d("NarsGeoman", "startEditing: ${feature.id}, type=${feature.type}")
        geoman.disableAllModes()
        _isDrawing.value = false

        eventHandler.setEditingFeature(feature.id, feature)
        _isEditing.value = true

        val geoJsonFeature = geometryConverter.convertToGeoJson(feature)
        geoman.addGeoJsonFeature(geoJsonFeature)

        geoman.enableEdit(EditModeName.CHANGE)
        geoman.startEditingFeature(geometryConverter.convertToGeomanFeatureData(feature))
    }

    fun stopEditing() {
        _isEditing.value = false
        eventHandler.setEditingFeature(null, null)
        geoman.disableAllModes()
    }

    fun commitEdits() {
        val originalFeature = eventHandler.getEditingFeature() ?: return

        val sourceNames = listOf(
            GeomanConstants.SOURCE_MARKERS, GeomanConstants.SOURCE_LINES,
            GeomanConstants.SOURCE_POLYGONS, GeomanConstants.SOURCE_CIRCLES
        )

        var updatedGeometry: com.nars.maplibre.data.model.Geometry? = null
        for (sourceName in sourceNames) {
            val featureData = geoman.features.getFeature(sourceName, originalFeature.id)
            if (featureData != null) {
                updatedGeometry = eventHandler.extractGeometryFromFeatureData(featureData)
                break
            }
        }

        val updated = if (updatedGeometry != null) {
            originalFeature.copy(geometry = updatedGeometry)
        } else {
            Log.w("NarsGeoman", "Could not find updated geometry for ${originalFeature.id}")
            originalFeature
        }
        onFeatureUpdated(updated)
        stopEditing()
    }

    fun cancelEdits() {
        val featureId = eventHandler.getEditingFeatureId() ?: return
        val sourceNames = listOf(
            GeomanConstants.SOURCE_MARKERS, GeomanConstants.SOURCE_LINES,
            GeomanConstants.SOURCE_POLYGONS, GeomanConstants.SOURCE_CIRCLES
        )
        for (sourceName in sourceNames) {
            val featureData = geoman.features.getFeature(sourceName, featureId)
            if (featureData != null) {
                geoman.features.removeFeature(sourceName, featureId)
                break
            }
        }
        stopEditing()
    }

    fun addFeature(feature: NarsFeature) {
        featureRenderer.addFeature(feature)
        val geoJsonFeature = geometryConverter.convertToGeoJson(feature)
        geoman.addGeoJsonFeature(geoJsonFeature, geometryConverter.getSourceNameForGeometry(feature.geometry))
    }

    fun addFeatures(features: List<NarsFeature>) {
        val currentPhaseKey = currentPhase?.key
        val filtered = if (currentPhaseKey != null) features.filter { it.properties.phase == currentPhaseKey } else features
        filtered.forEach { addFeature(it) }
    }

    fun updateDisplayedFeatures(allFeatures: List<NarsFeature>) {
        clearAllFeatures()
        addFeatures(allFeatures)
        if (currentPhase?.key == "roads") {
            featureRenderer.addRoadEndpointMarkers(allFeatures)
        }
    }

    fun updateFeatureOnMap(feature: NarsFeature) {
        removeFeature(feature.id)
        addFeature(feature)
    }

    fun updateFeatureId(oldId: String, newId: String) {
        Log.d("NarsGeoman", "Updating feature ID: $oldId -> $newId")
    }

    fun removeFeature(featureId: String) {
        val sourceNames = listOf(
            GeomanConstants.SOURCE_MARKERS, GeomanConstants.SOURCE_LINES,
            GeomanConstants.SOURCE_POLYGONS, GeomanConstants.SOURCE_CIRCLES
        )
        for (sourceName in sourceNames) {
            val featureData = geoman.features.getFeature(sourceName, featureId)
            if (featureData != null) {
                geoman.features.removeFeature(sourceName, featureId)
                break
            }
        }

        val layerName = "nars_layer_$featureId"
        try { map.style?.getLayer(layerName)?.let { map.style?.removeLayer(it) } } catch (e: Exception) {}
        try { map.style?.getLayer("${layerName}_outline")?.let { map.style?.removeLayer(it) } } catch (e: Exception) {}
        try { map.style?.getLayer("${layerName}_stroke")?.let { map.style?.removeLayer(it) } } catch (e: Exception) {}
        try { map.style?.getLayer("${layerName}_label")?.let { map.style?.removeLayer(it) } } catch (e: Exception) {}
        try { map.style?.removeSource("nars_${featureId}_edges") } catch (e: Exception) {}
        try { map.style?.removeSource("nars_$featureId") } catch (e: Exception) {}

        featureRenderer.removeVertexMarkers(featureId)
        featureRenderer.removeFromTracking(featureId)
        Log.d("NarsGeoman", "Removed feature $featureId")
    }

    fun clearAllFeatures() {
        geoman.clearAllFeatures()
        featureRenderer.clearTracking()
    }

    fun snapPoint(point: LatLng, features: List<NarsFeature>, snapThresholdMeters: Double = 20.0): LatLng {
        return snappingEngine.snapPoint(point, features, snapThresholdMeters)
    }

    fun onMapClick(latLng: LatLng) {
        if (_isDrawing.value) {
            val enabledModes = geoman.getEnabledModes()
            val drawMode = enabledModes.find { it.first == com.geoman.maplibre.geoman.types.ModeType.DRAW }
            drawMode?.let { (_, modeName) ->
                geoman.handleDrawClick(modeName, latLng)
            }
        } else if (_isEditing.value) {
            val enabledModes = geoman.getEnabledModes()
            val editMode = enabledModes.find { it.first == com.geoman.maplibre.geoman.types.ModeType.EDIT }
            editMode?.let { (_, modeName) ->
                geoman.handleEditClick(modeName, latLng)
            }
        }
    }

    fun onMapLongClick(latLng: LatLng) {
        if (_isDrawing.value) {
            val enabledModes = geoman.getEnabledModes()
            val drawMode = enabledModes.find { it.first == com.geoman.maplibre.geoman.types.ModeType.DRAW }
            drawMode?.let { (_, modeName) ->
                geoman.handleDrawLongPress(modeName, latLng)
            }
        }
    }

    fun destroy() {
        stopDrawing()
        stopEditing()
        geoman.destroy()
    }
}
