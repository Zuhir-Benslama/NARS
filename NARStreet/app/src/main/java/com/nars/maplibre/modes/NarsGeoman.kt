package com.nars.maplibre.modes

import android.content.Context
import com.nars.maplibre.utils.NarsLogger
import com.geoman.maplibre.geoman.Geoman
import com.geoman.maplibre.geoman.core.options.GmOptionsData
import com.geoman.maplibre.geoman.core.options.SettingsOptions
import com.geoman.maplibre.geoman.core.GeomanCoreConstants
import com.geoman.maplibre.geoman.types.DrawModeName
import com.geoman.maplibre.geoman.types.EditModeName
import com.nars.maplibre.data.model.DrawType
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.utils.Config
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView

@Suppress("DEPRECATION")
class NarsGeoman internal constructor(
    val geoman: Geoman,
    private val displayManager: FeatureDisplayManager,
    private val eventHandler: GeomanEventHandler,
    private val geometryConverter: GeometryConverter,
    private val snappingEngine: SnappingEngine,
    private val onFeatureCreated: (NarsFeature) -> Unit,
    private val onFeatureUpdated: (NarsFeature) -> Unit,
    private val onFeatureDeleted: (String) -> Unit,
    private val map: MapLibreMap? = null
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var destroyed = false
    private var currentPhase: PhaseDefinition? = null

    private val _isDrawing = MutableStateFlow(false)
    val isDrawing: StateFlow<Boolean> = _isDrawing.asStateFlow()

    private val _isEditing = MutableStateFlow(false)
    val isEditing: StateFlow<Boolean> = _isEditing.asStateFlow()

    companion object {
        operator fun invoke(
            mapView: MapView,
            map: MapLibreMap,
            context: Context,
            onFeatureCreated: (NarsFeature) -> Unit,
            onFeatureUpdated: (NarsFeature) -> Unit,
            onFeatureDeleted: (String) -> Unit
        ): NarsGeoman {
            val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
            val options = GmOptionsData(
                settings = SettingsOptions(
                    useControlsUi = true,
                    showControlsOnMap = false,
                    enableSnap = true,
                    snapDistance = Config.SNAP_THRESHOLD_PX.toFloat()
                )
            )
            val geoman = Geoman(mapView, map, options)
            val featureRenderer = FeatureRenderer(map).also { renderer ->
                renderer.labelAndMarkerManager = LabelAndMarkerManager(map)
            }
            val geometryConverter = GeometryConverter()
            val displayManager = FeatureDisplayManager(geoman, featureRenderer, geometryConverter, map)
            val eventHandler = GeomanEventHandler(scope, geoman, onFeatureCreated, onFeatureUpdated, onFeatureDeleted)
            eventHandler.setupEventListeners()
            NarsLogger.d(
                "NarsGeoman",
                "Initialized — delegating display to FeatureDisplayManager"
            )
            return NarsGeoman(
                geoman = geoman,
                displayManager = displayManager,
                eventHandler = eventHandler,
                geometryConverter = geometryConverter,
                snappingEngine = SnappingEngine(),
                onFeatureCreated = onFeatureCreated,
                onFeatureUpdated = onFeatureUpdated,
                onFeatureDeleted = onFeatureDeleted,
                map = map
            )
        }
    }

    fun setCurrentPhase(phase: PhaseDefinition) {
        currentPhase = phase
        displayManager.currentPhase = phase
        eventHandler.setCurrentPhase(phase)
    }

    fun startDrawing() {
        val phase = currentPhase ?: return
        NarsLogger.d("NarsGeoman", "startDrawing for phase: ${phase.label}, drawType: ${phase.drawType}")

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
        NarsLogger.d("NarsGeoman", "startEditing: ${feature.id}, type=${feature.type}")
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
            GeomanCoreConstants.SOURCE_MARKERS, GeomanCoreConstants.SOURCE_LINES,
            GeomanCoreConstants.SOURCE_POLYGONS, GeomanCoreConstants.SOURCE_CIRCLES
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
            NarsLogger.w("NarsGeoman", "Could not find updated geometry for ${originalFeature.id}")
            originalFeature
        }
        onFeatureUpdated(updated)
        stopEditing()
    }

    fun cancelEdits() {
        val featureId = eventHandler.getEditingFeatureId() ?: return
        val sourceNames = listOf(
            GeomanCoreConstants.SOURCE_MARKERS, GeomanCoreConstants.SOURCE_LINES,
            GeomanCoreConstants.SOURCE_POLYGONS, GeomanCoreConstants.SOURCE_CIRCLES
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

    fun addFeature(feature: NarsFeature) = displayManager.addFeature(feature)

    fun addFeatures(features: List<NarsFeature>) = displayManager.addFeatures(features)

    fun updateDisplayedFeatures(allFeatures: List<NarsFeature>) = displayManager.updateDisplayedFeatures(allFeatures)

    fun updateFeatureId(oldId: String, newId: String) = displayManager.updateFeatureId(oldId, newId)

    fun updateFeatureOnMap(feature: NarsFeature) = displayManager.updateFeatureOnMap(feature)

    fun removeFeature(featureId: String) = displayManager.removeFeature(featureId)

    fun clearAllFeatures() = displayManager.clearAllFeatures()

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
        if (destroyed) return
        destroyed = true
        stopDrawing()
        stopEditing()
        geoman.destroy()
        scope.cancel()
    }
}
