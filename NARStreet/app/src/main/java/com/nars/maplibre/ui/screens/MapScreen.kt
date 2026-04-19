package com.nars.maplibre.ui.screens

import android.util.Log
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nars.maplibre.NarsApplication
import com.nars.maplibre.NarsViewModel
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.modes.NarsGeoman
import com.nars.maplibre.ui.components.CompactBaseLayerSelector
import com.nars.maplibre.ui.components.CompactInfoPanel
import com.nars.maplibre.ui.components.CompactPhaseSelector
import com.nars.maplibre.ui.components.DrawingControls
import com.nars.maplibre.ui.components.FeatureModal
import com.nars.maplibre.ui.components.InfoPanel
import com.nars.maplibre.ui.components.NarsMap
import com.nars.maplibre.ui.components.PhaseBar
import com.nars.maplibre.ui.components.ProfileMenu
import com.nars.maplibre.ui.components.TileControl
import com.nars.maplibre.ui.components.VerticalPhaseNav
import com.nars.maplibre.ui.theme.GlassBackground
import kotlinx.coroutines.launch
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView

/**
 * Main map screen for NARS
 * Matches nars-vite-maplibre layout with glass-morphism UI
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MapScreen(
    viewModel: NarsViewModel,
    onNavigateToSettings: () -> Unit,
    onLogout: () -> Unit
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }
    val application = NarsApplication.instance

    // Collect state flows
    val currentPhase by viewModel.currentPhase.collectAsState()
    val allFeatures by viewModel.allFeatures.collectAsState()
    val selectedFeature by viewModel.selectedFeature.collectAsState()
    val baseLayer by viewModel.baseLayer.collectAsState()
    val uiState by viewModel.uiState.collectAsState()
    val drawingEnabled by viewModel.drawingEnabled.collectAsState()
    val editModeEnabled by viewModel.editModeEnabled.collectAsState()

    // UI state
    var showFeatureModal by remember { mutableStateOf(false) }
    var editingFeature by remember { mutableStateOf<NarsFeature?>(null) }

    // NarsGeoman instance
    var narsGeoman by remember { mutableStateOf<NarsGeoman?>(null) }
    var mapView by remember { mutableStateOf<MapView?>(null) }
    var mapLibreMap by remember { mutableStateOf<MapLibreMap?>(null) }

    // Feature counts
    val featureCounts = remember(allFeatures) {
        allFeatures.groupingBy { it.properties.phase }
            .eachCount()
    }

    // Handle snackbar messages
    LaunchedEffect(uiState.errorMessage) {
        uiState.errorMessage?.let { message ->
            snackbarHostState.showSnackbar(message)
            viewModel.clearError()
        }
    }

    LaunchedEffect(uiState.successMessage) {
        uiState.successMessage?.let { message ->
            snackbarHostState.showSnackbar(message)
            viewModel.clearSuccess()
        }
    }

    // Load features on map ready
    LaunchedEffect(narsGeoman) {
        Log.d("MapScreen", "LaunchedEffect triggered, narsGeoman = ${narsGeoman != null}")
        narsGeoman?.let { geoman ->
            Log.d("MapScreen", "Loading features from backend...")
            Log.d("MapScreen", "API URL: ${application.apiClient.let { "initialized" }}")
            Log.d("MapScreen", "Logged in: ${application.isLoggedIn()}")

            // Set loading state
            viewModel.setLoading(true)

            try {
                // Check if logged in
                if (!application.isLoggedIn()) {
                    Log.w("MapScreen", "User not logged in, cannot load features")
                    viewModel.showError("Please login first")
                    viewModel.setLoading(false)
                    return@let
                }

                // Load features from backend
                val result = application.apiClient.loadFeatures()
                Log.d("MapScreen", "Load result: success=${result.isSuccess}, features count = ${result.getOrNull()?.size}")

                result.onSuccess { features ->
                    Log.d("MapScreen", "Features loaded from API: ${features.size}")
                    features.forEach { f ->
                        Log.d("MapScreen", "  - ${f.id}: ${f.properties.phase}, type=${f.type}, geom=${f.geometry::class.simpleName}")
                    }

                    // Add features to store and map
                    viewModel.featureStore.addFeatures(features)

                    // Force update display for current phase
                    geoman.updateDisplayedFeatures(features)

                    if (features.isEmpty()) {
                        viewModel.showSuccess("No features found - start drawing to create your first feature")
                    } else {
                        viewModel.showSuccess("Loaded ${features.size} features")
                    }
                }
                result.onFailure { error ->
                    Log.e("MapScreen", "Failed to load features: ${error.message}")
                    viewModel.showError("Failed to load features: ${error.message}")
                }
            } catch (e: Exception) {
                Log.e("MapScreen", "Error loading features: ${e.message}", e)
                viewModel.showError("Error: ${e.message}")
            } finally {
                // Always clear loading state
                viewModel.setLoading(false)
            }
        }
    }

    // Handle feature creation from drawing — add to map immediately, then open modal
    fun handleFeatureCreated(feature: NarsFeature) {
        Log.d("MapScreen", "handleFeatureCreated: ${feature.id}, phase=${feature.properties.phase}")
        // Add feature to map immediately so user sees their drawn shape
        narsGeoman?.addFeature(feature)
        // Store as pending feature and open modal for user to fill in details
        editingFeature = feature
        showFeatureModal = true
    }

    // Initialize NarsGeoman when map is ready
    val initializeNarsGeoman: (MapView, MapLibreMap) -> Unit = { mv, map ->
        mapView = mv
        mapLibreMap = map

        narsGeoman = NarsGeoman(
            mapView = mv,
            map = map,
            context = context,
            onFeatureCreated = { feature ->
                handleFeatureCreated(feature)
            },
            onFeatureUpdated = { feature ->
                viewModel.updateFeature(feature)
                scope.launch {
                    snackbarHostState.showSnackbar("Feature updated successfully")
                }
            },
            onFeatureDeleted = { featureId ->
                viewModel.deleteFeature(featureId)
                scope.launch {
                    snackbarHostState.showSnackbar("Feature deleted successfully")
                }
            }
        )

        currentPhase?.let { narsGeoman?.setCurrentPhase(it) }

        // Fly camera to user's commune if available
        application.appPreferences.user?.let { user ->
            if (user.hasCommuneLocation()) {
                val lat = user.communeLatitude!!
                val lng = user.communeLongitude!!
                map.animateCamera(
                    org.maplibre.android.camera.CameraUpdateFactory.newLatLngZoom(
                        org.maplibre.android.geometry.LatLng(lat, lng),
                        14.0
                    ),
                    1500
                )
            }
        }
    }

    // Handle map click for feature selection AND drawing/editing
    fun handleMapClick(latLng: org.maplibre.android.geometry.LatLng) {
        Log.d("MapScreen", "handleMapClick: drawingEnabled=$drawingEnabled, editModeEnabled=$editModeEnabled")

        // Forward clicks to Geoman when in drawing or editing mode
        if (drawingEnabled || editModeEnabled) {
            // Snap to nearby features during drawing
            val snappedLatLng = if (drawingEnabled) {
                narsGeoman?.snapPoint(latLng, allFeatures) ?: latLng
            } else {
                latLng
            }
            Log.d("MapScreen", "Forwarding click to Geoman for drawing/editing (snapped: $snappedLatLng)")
            narsGeoman?.onMapClick(snappedLatLng)
            return
        }

        // Find clicked feature — only features of the current phase
        val currentPhaseKey = currentPhase?.key
        val clickedFeature = allFeatures
            .filter { it.properties.phase == currentPhaseKey }
            .firstOrNull { feature ->
            when (val geometry = feature.geometry) {
                is com.nars.maplibre.data.model.PointGeometry -> {
                    val featurePoint = org.maplibre.android.geometry.LatLng(
                        geometry.coordinates[1],
                        geometry.coordinates[0]
                    )
                    latLng.distanceTo(featurePoint) < 20.0
                }
                is com.nars.maplibre.data.model.CircleGeometry -> {
                    val centerPoint = org.maplibre.android.geometry.LatLng(
                        geometry.coordinates[1],
                        geometry.coordinates[0]
                    )
                    latLng.distanceTo(centerPoint) < (geometry.coordinates[2].coerceAtLeast(10.0))
                }
                is com.nars.maplibre.data.model.LineStringGeometry -> {
                    val coords = geometry.coordinates.chunked(2)
                    coords.any { coord ->
                        val linePoint = org.maplibre.android.geometry.LatLng(coord[1], coord[0])
                        latLng.distanceTo(linePoint) < 20.0
                    }
                }
                is com.nars.maplibre.data.model.PolygonGeometry -> {
                    val coords = geometry.coordinates.chunked(2)
                    coords.any { coord ->
                        val polyPoint = org.maplibre.android.geometry.LatLng(coord[1], coord[0])
                        latLng.distanceTo(polyPoint) < 20.0
                    }
                }
            }
        }

        if (clickedFeature != null) {
            Log.d("MapScreen", "Feature clicked: ${clickedFeature.id}")
            viewModel.selectFeature(clickedFeature)
        } else {
            Log.d("MapScreen", "No feature clicked - clearing selection")
            viewModel.clearSelection()
        }
    }

    // Handle map long click - forward to Geoman for finishing drawing
    fun handleMapLongClick(latLng: org.maplibre.android.geometry.LatLng) {
        Log.d("MapScreen", "handleMapLongClick: drawingEnabled=$drawingEnabled")
        if (drawingEnabled) {
            narsGeoman?.onMapLongClick(latLng)
        }
    }

    // Handle phase change
    LaunchedEffect(currentPhase) {
        currentPhase?.let { phase ->
            Log.d("MapScreen", "Phase changed to: ${phase.label} (${phase.key})")
            narsGeoman?.setCurrentPhase(phase)
            // Update displayed features for new phase
            narsGeoman?.updateDisplayedFeatures(allFeatures)
            // Clear loading state after phase change
            viewModel.setLoading(false)
        }
    }

    // Handle drawing toggle
    fun toggleDrawing() {
        Log.d("MapScreen", "=== toggleDrawing called ===")
        Log.d("MapScreen", "Current drawingEnabled=$drawingEnabled, currentPhase=${currentPhase?.label}")
        if (drawingEnabled) {
            Log.d("MapScreen", "Stopping drawing")
            viewModel.toggleDrawing(false)
            narsGeoman?.stopDrawing()
        } else {
            Log.d("MapScreen", "Starting drawing for phase: ${currentPhase?.label} (${currentPhase?.drawType})")
            viewModel.toggleDrawing(true)
            viewModel.toggleEditMode(false)
            narsGeoman?.startDrawing()
            Log.d("MapScreen", "startDrawing called, narsGeoman isDrawing=${narsGeoman?.isDrawing?.value}")
        }
    }

    // Handle edit toggle
    fun toggleEditing() {
        Log.d("MapScreen", "=== toggleEditing called ===")
        Log.d("MapScreen", "Current editModeEnabled=$editModeEnabled, selectedFeature=$selectedFeature")
        if (editModeEnabled) {
            // Disable edit mode
            Log.d("MapScreen", "Stopping editing")
            viewModel.toggleEditMode(false)
            narsGeoman?.stopEditing()
            viewModel.clearSelection()
            editingFeature = null
            showFeatureModal = false
        } else {
            // Enable edit mode - need a selected feature first
            selectedFeature?.let { feature ->
                Log.d("MapScreen", "Starting editing for feature: ${feature.id}, type=${feature.type}")
                viewModel.toggleEditMode(true)
                viewModel.toggleDrawing(false)
                narsGeoman?.startEditing(feature)
                editingFeature = feature
                Log.d("MapScreen", "startEditing called, narsGeoman isEditing=${narsGeoman?.isEditing?.value}")
                // Don't open modal - user edits geometry directly on map
                scope.launch {
                    snackbarHostState.showSnackbar("Drag vertices to edit. Tap Save when done.")
                }
            } ?: run {
                Log.d("MapScreen", "No feature selected for editing")
                scope.launch {
                    snackbarHostState.showSnackbar("Please select a feature to edit first")
                }
            }
        }
    }

    // Handle feature save (from modal — both new features and edits)
    fun handleFeatureSave(feature: NarsFeature) {
        val existingFeature = editingFeature
        if (existingFeature != null && (existingFeature.dbId ?: 0L) != 0L) {
            // Editing existing feature — update geometry + properties
            viewModel.updateFeature(feature)
            narsGeoman?.commitEdits()
            // Re-render the feature on the map with updated properties
            narsGeoman?.updateFeatureOnMap(feature)

            // Save to backend
            scope.launch {
                try {
                    Log.d("MapScreen", "Updating feature in backend: ${feature.id}")
                    val result = application.apiClient.updateFeature(feature.id, feature)
                    result.onSuccess {
                        Log.d("MapScreen", "Feature updated in backend")
                        snackbarHostState.showSnackbar("Feature updated successfully")
                    }
                    result.onFailure { error ->
                        Log.e("MapScreen", "Failed to update feature in backend: ${error.message}")
                        snackbarHostState.showSnackbar("Failed to update feature: ${error.message}")
                    }
                } catch (e: Exception) {
                    Log.e("MapScreen", "Error updating feature: ${e.message}")
                    snackbarHostState.showSnackbar("Error updating feature: ${e.message}")
                }
            }
        } else if (existingFeature != null) {
            // New feature from drawing — already on map, just save to backend
            scope.launch {
                try {
                    Log.d("MapScreen", "Saving new feature to backend: ${feature.id}, phase=${feature.properties.phase}")
                    val result = application.apiClient.saveFeature(feature)
                    result.onSuccess { savedId ->
                        Log.d("MapScreen", "Feature saved with ID: $savedId")
                        val updatedFeature = if (savedId != 0L && savedId.toString() != feature.id) {
                            feature.copy(dbId = savedId, id = savedId.toString())
                        } else {
                            feature.copy(dbId = savedId)
                        }
                        // Update in ViewModel store
                        viewModel.addFeature(updatedFeature)
                        narsGeoman?.updateFeatureId(feature.id, updatedFeature.id)
                        // Re-render the feature on the map with updated properties (name label, etc.)
                        narsGeoman?.updateFeatureOnMap(updatedFeature)
                        snackbarHostState.showSnackbar("Feature saved successfully")
                    }
                    result.onFailure { error ->
                        Log.e("MapScreen", "Failed to save feature: ${error.message}")
                        snackbarHostState.showSnackbar("Failed to save feature: ${error.message}")
                    }
                } catch (e: Exception) {
                    Log.e("MapScreen", "Error saving feature: ${e.message}")
                    snackbarHostState.showSnackbar("Error saving feature: ${e.message}")
                }
            }
        }
        showFeatureModal = false
        editingFeature = null
    }

    // Handle feature delete
    fun handleFeatureDelete(featureId: String) {
        viewModel.deleteFeature(featureId)
        narsGeoman?.removeFeature(featureId)
        
        // Delete from backend asynchronously
        scope.launch {
            try {
                Log.d("MapScreen", "Deleting feature from backend: $featureId")
                val result = application.apiClient.deleteFeature(featureId)
                result.onSuccess {
                    Log.d("MapScreen", "Feature deleted from backend")
                    snackbarHostState.showSnackbar("Feature deleted from backend")
                }
                result.onFailure { error ->
                    Log.e("MapScreen", "Failed to delete feature from backend: ${error.message}")
                    snackbarHostState.showSnackbar("Failed to delete from backend: ${error.message}")
                }
            } catch (e: Exception) {
                Log.e("MapScreen", "Error deleting feature: ${e.message}")
                snackbarHostState.showSnackbar("Error deleting feature: ${e.message}")
            }
        }
        
        showFeatureModal = false
        editingFeature = null
        viewModel.clearSelection()
    }

    // Handle logout
    fun handleLogout() {
        scope.launch {
            application.logout()
            onLogout()
        }
    }

    Scaffold(
        snackbarHost = {
            SnackbarHost(hostState = snackbarHostState) { data ->
                Snackbar(
                    snackbarData = data,
                    containerColor = MaterialTheme.colorScheme.inverseSurface,
                    contentColor = MaterialTheme.colorScheme.inverseOnSurface
                )
            }
        }
    ) { paddingValues ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(GlassBackground)
                .padding(paddingValues)
        ) {
            // Map
            NarsMap(
                viewModel = viewModel,
                onMapReady = { mapView, map ->
                    initializeNarsGeoman(mapView, map)
                },
                onMapClick = { latLng ->
                    handleMapClick(latLng)
                },
                onMapLongClick = { latLng ->
                    handleMapLongClick(latLng)
                },
                shouldHandleClick = {
                    // Don't handle map clicks for feature selection when in drawing or editing mode
                    // This allows Geoman to process clicks for drawing/editing
                    !drawingEnabled && !editModeEnabled
                },
                modifier = Modifier.fillMaxSize()
            )

            // Left side - Drawing controls
            DrawingControls(
                currentPhase = currentPhase,
                isDrawing = drawingEnabled,
                isEditing = editModeEnabled,
                onDrawToggle = { toggleDrawing() },
                onEditToggle = { toggleEditing() },
                onSettingsClick = onNavigateToSettings,
                onUndoClick = { viewModel.undo() },
                onGenerateNamingPanels = { viewModel.generateNamingPanels() },
                onComputeRoadDirections = { viewModel.computeRoadDirections() },
                onSetHouseNumbers = { viewModel.setHouseNumbers() },
                canUndo = viewModel.canUndo,
                modifier = Modifier.align(Alignment.CenterStart)
            )

            // Profile menu (top-right)
            ProfileMenu(
                user = application.appPreferences.user,
                onSettingsClick = onNavigateToSettings,
                onLogoutClick = { handleLogout() },
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .padding(end = 12.dp, top = 12.dp)
            )

            // Right side - Vertical phase nav and Tile control
            Column(
                modifier = Modifier
                    .align(Alignment.CenterEnd)
                    .padding(end = 12.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                // Vertical phase navigation
                VerticalPhaseNav(
                    currentPhaseIndex = currentPhase?.let { Phases.getIndexByKey(it.key) } ?: 0,
                    phaseCounts = featureCounts,
                    onPhaseSelected = { phase ->
                        Log.d("MapScreen", "=== PHASE CLICKED ===")
                        Log.d("MapScreen", "Phase: ${phase.label} (${phase.key})")
                        Log.d("MapScreen", "Current phase before: ${currentPhase?.key}")
                        val result = viewModel.setCurrentPhase(phase)
                        if (result != null) {
                            scope.launch {
                                snackbarHostState.showSnackbar("Phase: ${phase.label}")
                            }
                        } else {
                            // Validation failed - error message already shown via ViewModel
                            scope.launch {
                                snackbarHostState.showSnackbar("Cannot advance: requirements not met")
                            }
                        }
                    },
                    modifier = Modifier.width(40.dp)
                )

                // Tile control (base layer selector)
                TileControl(
                    currentLayer = baseLayer,
                    onLayerSelected = { layer ->
                        viewModel.setBaseLayer(layer)
                    }
                )
            }

            // Bottom-left - Info panel (feature counts)
            CompactInfoPanel(
                featureCounts = featureCounts,
                totalFeatures = allFeatures.size,
                modifier = Modifier
                    .align(Alignment.BottomStart)
                    .padding(start = 12.dp, bottom = 12.dp)
                    .width(140.dp)
            )

            // Loading indicator
            if (uiState.isLoading) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(Color.Black.copy(alpha = 0.3f)),
                    contentAlignment = Alignment.Center
                ) {
                    CircularProgressIndicator(
                        color = MaterialTheme.colorScheme.primary
                    )
                }
            }

            // Selected feature info (bottom)
            selectedFeature?.let { feature ->
                Card(
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .fillMaxWidth()
                        .padding(16.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = CardDefaults.cardColors(
                        containerColor = GlassBackground.copy(alpha = 0.9f)
                    ),
                    elevation = CardDefaults.cardElevation(defaultElevation = 4.dp)
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(12.dp)
                    ) {
                        // Feature info
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(
                                modifier = Modifier.weight(1f)
                            ) {
                                Text(
                                    text = feature.properties.name ?: feature.type.value,
                                    fontSize = 14.sp,
                                    color = MaterialTheme.colorScheme.onSurface
                                )
                                Text(
                                    text = feature.properties.phase,
                                    fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }

                            IconButton(
                                onClick = { viewModel.clearSelection() }
                            ) {
                                Icon(
                                    imageVector = Icons.Default.Close,
                                    contentDescription = "Close",
                                    tint = MaterialTheme.colorScheme.onSurface
                                )
                            }
                        }

                        // Action buttons - different based on edit mode
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 8.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            if (editModeEnabled && editingFeature?.id == feature.id) {
                                // In geometry edit mode - show Save and Cancel
                                Button(
                                    onClick = {
                                        narsGeoman?.commitEdits()
                                        viewModel.clearSelection()
                                        editingFeature = null
                                    },
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = MaterialTheme.colorScheme.primary
                                    ),
                                    modifier = Modifier.weight(1f)
                                ) {
                                    Icon(
                                        imageVector = Icons.Default.Edit,
                                        contentDescription = null,
                                        modifier = Modifier.size(18.dp)
                                    )
                                    Spacer(modifier = Modifier.width(4.dp))
                                    Text("Save Geometry")
                                }

                                Button(
                                    onClick = {
                                        narsGeoman?.cancelEdits()
                                        viewModel.clearSelection()
                                        editingFeature = null
                                    },
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = MaterialTheme.colorScheme.error
                                    ),
                                    modifier = Modifier.weight(1f)
                                ) {
                                    Text("Cancel")
                                }
                            } else {
                                // Normal mode - show Properties button
                                Button(
                                    onClick = {
                                        editingFeature = feature
                                        showFeatureModal = true
                                    },
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = MaterialTheme.colorScheme.primary
                                    ),
                                    modifier = Modifier.weight(1f)
                                ) {
                                    Text("Properties")
                                }

                                Button(
                                    onClick = { toggleEditing() },
                                    colors = ButtonDefaults.buttonColors(
                                        containerColor = MaterialTheme.colorScheme.secondary
                                    ),
                                    modifier = Modifier.weight(1f)
                                ) {
                                    Icon(
                                        imageVector = Icons.Default.Edit,
                                        contentDescription = null,
                                        modifier = Modifier.size(18.dp)
                                    )
                                    Spacer(modifier = Modifier.width(4.dp))
                                    Text("Edit Geometry")
                                }
                            }
                        }
                    }
                }
            }

            // Feature modal
            if (showFeatureModal && currentPhase != null) {
                FeatureModal(
                    feature = editingFeature,
                    phase = currentPhase!!,
                    onSave = { handleFeatureSave(it) },
                    onDismiss = {
                        // If this was a new feature (not yet saved), remove it from the map
                        editingFeature?.let { f ->
                            if (f.dbId == 0L) {
                                narsGeoman?.removeFeature(f.id)
                                viewModel.deleteFeature(f.id)
                            }
                        }
                        showFeatureModal = false
                        editingFeature = null
                        narsGeoman?.cancelEdits()
                    }
                )
            }
        }
    }
}
