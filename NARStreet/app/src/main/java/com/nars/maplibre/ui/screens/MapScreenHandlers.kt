package com.nars.maplibre.ui.screens

import com.nars.maplibre.utils.NarsLogger
import android.content.Context
import com.nars.maplibre.MapViewModel
import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.data.api.SessionManager
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.modes.NarsGeoman
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

class MapScreenHandlers(
    private val viewModel: MapViewModel,
    private val apiService: ApiService,
    private val sessionManager: SessionManager,
    private val context: Context,
    private val scope: CoroutineScope,
    private val snackbar: (String) -> Unit
) {
    companion object {
        private const val TAG = "MapScreenHandlers"
        private const val ANIM_DURATION_MS = 1500
        private const val MAP_ZOOM = 14.0
        private const val LONG_CLICK_DISTANCE_THRESHOLD = 50.0
        private const val NEAR_FEATURE_DISTANCE_THRESHOLD = 20.0
    }

    var narsGeoman: NarsGeoman? = null

    val initializeNarsGeoman: (org.maplibre.android.maps.MapView, org.maplibre.android.maps.MapLibreMap) -> Unit =
        { mv, map ->
            val geoman = NarsGeoman(
                mapView = mv, map = map,
                context = context,
                onFeatureCreated = { feature -> handleFeatureCreated(feature) },
                onFeatureUpdated = { feature ->
                    viewModel.updateFeature(feature)
                    snackbar("Feature updated successfully")
                },
                onFeatureDeleted = { featureId ->
                    viewModel.deleteFeature(featureId)
                    snackbar("Feature deleted successfully")
                }
            )
            narsGeoman = geoman

            viewModel.currentPhase.value?.let { geoman.setCurrentPhase(it) }

            sessionManager.getUser()?.let { user ->
                if (user.hasCommuneLocation()) {
                    val lat = user.communeLatitude ?: return@let
                    val lng = user.communeLongitude ?: return@let
                    map.animateCamera(
                        org.maplibre.android.camera.CameraUpdateFactory.newLatLngZoom(
                            org.maplibre.android.geometry.LatLng(lat, lng), MAP_ZOOM
                        ), ANIM_DURATION_MS
                    )
                }
            }
        }

    fun handleFeatureCreated(feature: NarsFeature) {
        NarsLogger.d(TAG, "Feature created: ${feature.id}, phase=${feature.properties.phase}")
        narsGeoman?.addFeature(feature)
    }

    fun handleMapClick(latLng: org.maplibre.android.geometry.LatLng, drawingEnabled: Boolean, editModeEnabled: Boolean) {
        if (drawingEnabled || editModeEnabled) {
            val snapped = if (drawingEnabled) {
                narsGeoman?.snapPoint(latLng, viewModel.allFeatures.value) ?: latLng
            } else latLng
            narsGeoman?.onMapClick(snapped)
            return
        }

        val currentPhaseKey = viewModel.currentPhase.value?.key
        val clickedFeature = viewModel.allFeatures.value
            .filter { it.properties.phase == currentPhaseKey }
            .firstOrNull { feature -> isPointNearFeature(latLng, feature) }

        if (clickedFeature != null) viewModel.selectFeature(clickedFeature)
        else viewModel.clearSelection()
    }

    fun handleMapLongClick(latLng: org.maplibre.android.geometry.LatLng): NarsFeature? {
        val currentPhaseKey = viewModel.currentPhase.value?.key
        val clickedFeature = viewModel.allFeatures.value
            .filter { it.properties.phase == currentPhaseKey }
            .firstOrNull { feature ->
                when (val geometry = feature.geometry) {
                    is com.nars.maplibre.data.model.PointGeometry -> {
                        val fp = org.maplibre.android.geometry.LatLng(
                            geometry.coordinates[1], geometry.coordinates[0]
                        )
                        latLng.distanceTo(fp) < LONG_CLICK_DISTANCE_THRESHOLD
                    }
                    is com.nars.maplibre.data.model.LineStringGeometry -> {
                        geometry.coordinates.chunked(2).any { coord ->
                            val lp = org.maplibre.android.geometry.LatLng(coord[1], coord[0])
                            latLng.distanceTo(lp) < LONG_CLICK_DISTANCE_THRESHOLD
                        }
                    }
                    else -> false
                }
            }

        if (clickedFeature != null) viewModel.selectFeature(clickedFeature)
        return clickedFeature
    }

    fun toggleDrawing(currentDrawingEnabled: Boolean) {
        if (currentDrawingEnabled) {
            viewModel.toggleDrawing(false)
            narsGeoman?.stopDrawing()
        } else {
            viewModel.toggleDrawing(true)
            viewModel.toggleEditMode(false)
            narsGeoman?.startDrawing()
        }
    }

    fun toggleEditing(currentEditEnabled: Boolean) {
        if (currentEditEnabled) {
            viewModel.toggleEditMode(false)
            narsGeoman?.stopEditing()
            viewModel.clearSelection()
        } else {
            viewModel.selectedFeature.value?.let { feature ->
                viewModel.toggleEditMode(true)
                viewModel.toggleDrawing(false)
                narsGeoman?.startEditing(feature)
                snackbar("Drag vertices to edit. Tap Save when done.")
            } ?: snackbar("Please select a feature to edit first")
        }
    }

    fun saveFeature(feature: NarsFeature) {
        scope.launch {
            try {
                val result = apiService.saveFeature(feature)
                result.onSuccess { savedId ->
                    val updatedFeature = if (savedId != feature.id) {
                        feature.copy(dbId = savedId, id = savedId)
                    } else feature.copy(dbId = savedId)
                    viewModel.addFeature(updatedFeature)
                    narsGeoman?.updateFeatureId(feature.id, updatedFeature.id)
                    narsGeoman?.updateFeatureOnMap(updatedFeature)
                    snackbar("Feature saved successfully")
                }
                result.onFailure { snackbar("Failed to save feature: ${it.message}") }
            } catch (e: Exception) { snackbar("Error saving feature: ${e.message}") }
        }
    }

    fun updateFeature(feature: NarsFeature) {
        scope.launch {
            try {
                val result = apiService.updateFeature(feature.id, feature)
                result.onSuccess { snackbar("Feature updated successfully") }
                result.onFailure { snackbar("Failed to update feature: ${it.message}") }
            } catch (e: Exception) { snackbar("Error updating feature: ${e.message}") }
        }
    }

    fun deleteFeature(featureId: String) {
        viewModel.deleteFeature(featureId)
        narsGeoman?.removeFeature(featureId)
        scope.launch {
            try {
                val result = apiService.deleteFeature(featureId)
                result.onSuccess { snackbar("Feature deleted from backend") }
                result.onFailure { snackbar("Failed to delete from backend: ${it.message}") }
            } catch (e: Exception) { snackbar("Error deleting feature: ${e.message}") }
        }
    }

    fun logout(onLogout: () -> Unit) {
        scope.launch {
            sessionManager.logout()
            onLogout()
        }
    }

    fun loadFeaturesOnMapReady() {
        scope.launch {
            NarsLogger.d(TAG, "Loading features from backend...")
            viewModel.setLoading(true)
            try {
                val result = apiService.loadFeatures()
                result.onSuccess { features ->
                    viewModel.featureStore.addFeatures(features)
                    narsGeoman?.updateDisplayedFeatures(features)
                    snackbar(if (features.isEmpty()) "No features found" else "Loaded ${features.size} features")
                }
                result.onFailure { snackbar("Failed to load features: ${it.message}") }
            } catch (e: Exception) { snackbar("Error: ${e.message}") }
            finally { viewModel.setLoading(false) }
        }
    }

    private fun isPointNearFeature(latLng: org.maplibre.android.geometry.LatLng, feature: NarsFeature): Boolean {
        val threshold = NEAR_FEATURE_DISTANCE_THRESHOLD
        return when (val geometry = feature.geometry) {
            is com.nars.maplibre.data.model.PointGeometry -> {
                val fp = org.maplibre.android.geometry.LatLng(geometry.coordinates[1], geometry.coordinates[0])
                latLng.distanceTo(fp) < threshold
            }
            is com.nars.maplibre.data.model.CircleGeometry -> {
                val cp = org.maplibre.android.geometry.LatLng(geometry.coordinates[1], geometry.coordinates[0])
                latLng.distanceTo(cp) < geometry.coordinates[2].coerceAtLeast(10.0)
            }
            is com.nars.maplibre.data.model.LineStringGeometry -> {
                geometry.coordinates.chunked(2).any { coord ->
                    val lp = org.maplibre.android.geometry.LatLng(coord[1], coord[0])
                    latLng.distanceTo(lp) < threshold
                }
            }
            is com.nars.maplibre.data.model.PolygonGeometry -> {
                geometry.coordinates.chunked(2).any { coord ->
                    val pp = org.maplibre.android.geometry.LatLng(coord[1], coord[0])
                    latLng.distanceTo(pp) < threshold
                }
            }
        }
    }
}
