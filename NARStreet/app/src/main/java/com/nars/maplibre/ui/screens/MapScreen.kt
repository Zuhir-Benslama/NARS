package com.nars.maplibre.ui.screens

import com.nars.maplibre.utils.NarsLogger
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nars.maplibre.MapViewModel
import com.nars.maplibre.R
import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.data.api.SessionManager
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.ui.components.CompactInfoPanel
import com.nars.maplibre.ui.components.FeatureValidationModal
import com.nars.maplibre.ui.components.NarsMap
import com.nars.maplibre.ui.components.ProfileMenu
import com.nars.maplibre.ui.components.TileControl
import com.nars.maplibre.ui.components.VerticalPhaseNav
import com.nars.maplibre.ui.theme.GlassBackground
import kotlinx.coroutines.launch
import org.koin.androidx.compose.koinViewModel
import org.koin.java.KoinJavaComponent.get

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MapScreen(
    onNavigateToSettings: () -> Unit,
    onLogout: () -> Unit
) {
    val scope = rememberCoroutineScope()
    val snackbarHostState = remember { SnackbarHostState() }

    val context = androidx.compose.ui.platform.LocalContext.current
    val viewModel: MapViewModel = koinViewModel()
    val apiService: ApiService = get(ApiService::class.java)
    val sessionManager: SessionManager = get(SessionManager::class.java)

    val currentPhase by viewModel.currentPhase.collectAsState()
    val allFeatures by viewModel.allFeatures.collectAsState()
    val selectedFeature by viewModel.selectedFeature.collectAsState()
    val baseLayer by viewModel.baseLayer.collectAsState()
    val uiState by viewModel.uiState.collectAsState()
    val drawingEnabled by viewModel.drawingEnabled.collectAsState()
    val editModeEnabled by viewModel.editModeEnabled.collectAsState()

    var showFeatureModal by remember { mutableStateOf(false) }
    var editingFeature by remember { mutableStateOf<NarsFeature?>(null) }

    val handlers = remember {
        MapScreenHandlers(viewModel, apiService, sessionManager, context, scope) { msg ->
            scope.launch { snackbarHostState.showSnackbar(msg) }
        }
    }

    val featureCounts = remember(allFeatures) {
        allFeatures.groupingBy { it.properties.phase }.eachCount()
    }

    LaunchedEffect(uiState.errorMessage) {
        uiState.errorMessage?.let { snackbarHostState.showSnackbar(it); viewModel.clearError() }
    }
    LaunchedEffect(uiState.successMessage) {
        uiState.successMessage?.let { snackbarHostState.showSnackbar(it); viewModel.clearSuccess() }
    }

    LaunchedEffect(handlers.narsGeoman) {
        handlers.narsGeoman?.let { handlers.loadFeaturesOnMapReady() }
    }

    LaunchedEffect(currentPhase) {
        currentPhase?.let { phase ->
            NarsLogger.d("MapScreen", "Phase changed: ${phase.label}")
            handlers.narsGeoman?.setCurrentPhase(phase)
            handlers.narsGeoman?.updateDisplayedFeatures(allFeatures)
            viewModel.setLoading(false)
        }
    }

    fun handleFeatureSave(feature: NarsFeature) {
        val existing = editingFeature
        if (existing != null && existing.dbId != null) {
            viewModel.updateFeature(feature)
            handlers.narsGeoman?.commitEdits()
            handlers.narsGeoman?.updateFeatureOnMap(feature)
            handlers.updateFeature(feature)
        } else if (existing != null) {
            handlers.saveFeature(feature)
        }
        showFeatureModal = false
        editingFeature = null
    }

    Scaffold(
        snackbarHost = {
            SnackbarHost(hostState = snackbarHostState) { data ->
                Snackbar(snackbarData = data,
                    containerColor = MaterialTheme.colorScheme.inverseSurface,
                    contentColor = MaterialTheme.colorScheme.inverseOnSurface)
            }
        }
    ) { paddingValues ->
        Box(
            modifier = Modifier.fillMaxSize().background(GlassBackground).padding(paddingValues)
        ) {
            NarsMap(
                viewModel = viewModel,
                onMapReady = { mv, map ->
                    handlers.initializeNarsGeoman(mv, map)
                },
                onMapClick = { latLng -> handlers.handleMapClick(latLng, drawingEnabled, editModeEnabled) },
                onMapLongClick = { latLng ->
                    val clicked = handlers.handleMapLongClick(latLng)
                    if (clicked != null) { editingFeature = clicked; showFeatureModal = true }
                },
                shouldHandleClick = { !drawingEnabled && !editModeEnabled },
                modifier = Modifier.fillMaxSize()
            )

            ProfileMenu(
                user = sessionManager.getUser(),
                onSettingsClick = onNavigateToSettings,
                onLogoutClick = { handlers.logout(onLogout) },
                modifier = Modifier.align(Alignment.TopEnd).padding(end = 12.dp, top = 12.dp)
            )

            val phaseChangedText = stringResource(R.string.map_phase_changed)
            val cannotAdvanceText = stringResource(R.string.map_cannot_advance)
            Column(
                modifier = Modifier.align(Alignment.CenterEnd).padding(end = 12.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                VerticalPhaseNav(
                    currentPhaseIndex = currentPhase?.let { Phases.getIndexByKey(it.key) } ?: 0,
                    phaseCounts = featureCounts,
                    onPhaseSelected = { phase ->
                        viewModel.setCurrentPhase(phase)?.let {
                            scope.launch { snackbarHostState.showSnackbar("$phaseChangedText: ${phase.label}") }
                        } ?: scope.launch { snackbarHostState.showSnackbar(cannotAdvanceText) }
                    },
                    modifier = Modifier.width(40.dp)
                )
                TileControl(currentLayer = baseLayer, onLayerSelected = { viewModel.setBaseLayer(it) })
            }

            CompactInfoPanel(
                featureCounts = featureCounts, totalFeatures = allFeatures.size,
                modifier = Modifier.align(Alignment.BottomStart).padding(start = 12.dp, bottom = 12.dp).width(140.dp)
            )

            if (uiState.isLoading) {
                Box(
                    modifier = Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.3f)),
                    contentAlignment = Alignment.Center
                ) { CircularProgressIndicator(color = MaterialTheme.colorScheme.primary) }
            }

            selectedFeature?.let { feature ->
                Card(
                    modifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth().padding(16.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = CardDefaults.cardColors(containerColor = GlassBackground.copy(alpha = 0.9f)),
                    elevation = CardDefaults.cardElevation(defaultElevation = 4.dp)
                ) {
                    Column(modifier = Modifier.fillMaxWidth().padding(12.dp)) {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(text = feature.properties.name ?: feature.type.value, fontSize = 14.sp)
                                Text(text = feature.properties.phase, fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            IconButton(onClick = { viewModel.clearSelection() }) {
                                Icon(Icons.Default.Close, contentDescription = "Close")
                            }
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            if (editModeEnabled && editingFeature?.id == feature.id) {
                                Button(
                                    onClick = {
                                        handlers.narsGeoman?.commitEdits()
                                        viewModel.clearSelection(); editingFeature = null
                                    },
                                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary),
                                    modifier = Modifier.weight(1f)
                                ) { Icon(Icons.Default.Edit, contentDescription = null, modifier = Modifier.size(18.dp))
                                    Spacer(Modifier.width(4.dp)); Text(stringResource(R.string.map_save_geometry)) }
                                Button(
                                    onClick = {
                                        handlers.narsGeoman?.cancelEdits()
                                        viewModel.clearSelection(); editingFeature = null
                                    },
                                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                                    modifier = Modifier.weight(1f)
                                ) { Text(stringResource(R.string.map_cancel)) }
                            } else {
                                Button(
                                    onClick = { editingFeature = feature; showFeatureModal = true },
                                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary),
                                    modifier = Modifier.weight(1f)
                                ) { Text(stringResource(R.string.map_properties)) }
                                Button(
                                    onClick = { handlers.toggleEditing(editModeEnabled) },
                                    colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.secondary),
                                    modifier = Modifier.weight(1f)
                                ) { Icon(Icons.Default.Edit, contentDescription = null, modifier = Modifier.size(18.dp))
                                    Spacer(Modifier.width(4.dp)); Text(stringResource(R.string.map_edit_geometry)) }
                            }
                        }
                    }
                }
            }

            if (showFeatureModal && editingFeature != null && currentPhase != null) {
                editingFeature?.let { feature ->
                    currentPhase?.let { phase ->
                        FeatureValidationModal(
                            feature = feature, phase = phase,
                            onSave = { handleFeatureSave(it) },
                            onDismiss = { showFeatureModal = false; editingFeature = null }
                        )
                    }
                }
            }
        }
    }
}
