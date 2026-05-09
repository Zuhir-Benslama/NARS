package com.nars.maplibre

import com.nars.maplibre.utils.NarsLogger
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.maplibre.data.model.BaseLayerType
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.store.FeatureStore
import com.nars.maplibre.data.store.UndoAction
import com.nars.maplibre.ui.theme.ThemeMode
import com.nars.maplibre.utils.HouseNumberingManager
import com.nars.maplibre.utils.NamingPanelGenerator
import com.nars.maplibre.utils.PhaseNavigator
import com.nars.maplibre.utils.RoadDirectionsCalculator
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * Main ViewModel for NARS application
 * Manages app-wide state and coordinates between UI and data layers
 */
class NarsViewModel(
    val featureStore: FeatureStore,
    private val appPreferences: AppPreferences,
    private val application: NarsApplication
) : ViewModel() {

    // Phase navigator for validation
    private val phaseNavigator = PhaseNavigator(featureStore)

    // Road directions calculator
    private val roadDirectionsCalculator = RoadDirectionsCalculator()

    // Naming panel generator
    private val namingPanelGenerator = NamingPanelGenerator()

    // House numbering manager
    private val houseNumberingManager = HouseNumberingManager()

    // Current phase
    val currentPhase: StateFlow<PhaseDefinition?> = featureStore.currentPhase

    // Features
    val allFeatures: StateFlow<List<NarsFeature>> = featureStore.allFeatures
    private val _currentPhaseFeatures = MutableStateFlow<List<NarsFeature>>(emptyList())
    val currentPhaseFeatures: StateFlow<List<NarsFeature>> = _currentPhaseFeatures
    val selectedFeature: StateFlow<NarsFeature?> = featureStore.selectedFeature

    // UI state
    private val _uiState = MutableStateFlow(UiState())
    val uiState: StateFlow<UiState> = _uiState.asStateFlow()

    // Theme
    private val _themeMode = MutableStateFlow(appPreferences.themeMode)
    val themeMode: StateFlow<ThemeMode> = _themeMode.asStateFlow()

    // Base layer
    private val _baseLayer = MutableStateFlow(appPreferences.baseLayer)
    val baseLayer: StateFlow<BaseLayerType> = _baseLayer.asStateFlow()

    // Drawing enabled
    private val _drawingEnabled = MutableStateFlow(false)
    val drawingEnabled: StateFlow<Boolean> = _drawingEnabled.asStateFlow()

    // Edit mode enabled
    private val _editModeEnabled = MutableStateFlow(false)
    val editModeEnabled: StateFlow<Boolean> = _editModeEnabled.asStateFlow()

    // Reference state for house entrances
    val referenceRoadDbId: StateFlow<String?> = featureStore.referenceRoadDbId
    val referenceEntranceDbId: StateFlow<String?> = featureStore.referenceEntranceDbId

    // Undo state
    val canUndo: Boolean get() = featureStore.canUndo

    init {
        // Start with first phase (Roads for field mode)
        featureStore.setCurrentPhase(Phases.ALL.first())

        // Observe current phase features
        viewModelScope.launch {
            featureStore.currentPhase.collect { phase ->
                phase?.let {
                    _currentPhaseFeatures.value = featureStore.getFeaturesByPhase(it.key)
                }
            }
        }
    }

    /**
     * Set current phase with validation
     * Returns null if validation failed, phase if successful
     */
    fun setCurrentPhase(phase: PhaseDefinition): PhaseDefinition? {
        val currentIndex = featureStore.currentPhase.value?.index ?: 0

        // Only validate when advancing forward
        if (phase.index > currentIndex) {
            val error = phaseNavigator.canAdvance(phase.index)
            if (error != null) {
                showError(error)
                NarsLogger.d("NarsViewModel", "Phase validation failed: $error")
                return null
            }

            // Special case: compute road directions when leaving roads phase
            if (featureStore.currentPhase.value?.key == "roads" && phase.key != "roads") {
                computeRoadDirections()
            }
        }

        NarsLogger.d("NarsViewModel", "Setting current phase to: ${phase.label} (${phase.key})")
        featureStore.setCurrentPhase(phase)
        appPreferences.currentPhase = phase.key

        // Reset drawing and edit modes when changing phase
        _drawingEnabled.value = false
        _editModeEnabled.value = false

        return phase
    }

    /**
     * Navigate to next phase with validation
     */
    fun goToNextPhase(): PhaseDefinition? {
        val nextPhase = phaseNavigator.goNext()
        if (nextPhase == null) {
            val currentIndex = featureStore.currentPhase.value?.index ?: 0
            val error = phaseNavigator.canAdvance(currentIndex + 1)
            if (error != null) {
                showError(error)
            }
        }
        return nextPhase
    }

    /**
     * Navigate to previous phase
     */
    fun goToPreviousPhase(): PhaseDefinition? {
        return phaseNavigator.goBack()
    }

    /**
     * Check if can go to next phase
     */
    fun canGoNextPhase(): Boolean {
        return phaseNavigator.canGoForward()
    }

    /**
     * Set reference road for house entrance placement
     */
    fun setReferenceRoad(dbId: String?) {
        featureStore.setReferenceRoad(dbId)
    }

    /**
     * Set reference entrance for secondary entrance placement
     */
    fun setReferenceEntrance(dbId: String?) {
        featureStore.setReferenceEntrance(dbId)
    }

    /**
     * Compute road directions (field mode - no cityCenter needed)
     */
    fun computeRoadDirections() {
        viewModelScope.launch {
            try {
                val roads = featureStore.getFeaturesByPhase("roads")

                val result = roadDirectionsCalculator.computeDirectionsFromRoads(roads)

                // Apply reversals
                for (roadId in result.reversedRoadIds) {
                    val road = featureStore.getFeatureById(roadId) ?: continue
                    val geom = road.geometry as? com.nars.maplibre.data.model.LineStringGeometry
                    if (geom != null) {
                        val reversed = roadDirectionsCalculator.reverseRoadCoordinates(geom.coordinates)
                        val updatedRoad = road.copy(
                            geometry = com.nars.maplibre.data.model.LineStringGeometry(
                                type = "LineString",
                                coordinates = reversed
                            )
                        )
                        featureStore.updateFeature(road.id, updatedRoad)
                        // TODO: Save to backend
                    }
                }

                showSuccess(result.message)
                NarsLogger.d("NarsViewModel", "Road directions: ${result.message}")
            } catch (e: Exception) {
                NarsLogger.e("NarsViewModel", "Failed to compute road directions", e)
                showError("Failed to compute road directions")
            }
        }
    }

    /**
     * Generate naming panels (for field mode - only uses roads)
     */
    fun generateNamingPanels() {
        viewModelScope.launch {
            try {
                val roads = featureStore.getFeaturesByPhase("roads")

                val panels = namingPanelGenerator.generatePanelsFromRoads(roads)

                // Add panels to store and save to backend
                panels.forEach { panel ->
                    featureStore.addFeature(panel)
                    // Save to backend
                    try {
                        val saveResult = application.apiClient.saveFeature(panel)
                        saveResult.onSuccess { savedId ->
                            NarsLogger.d("NarsViewModel", "Saved naming panel: $savedId")
                        }
                    } catch (e: Exception) {
                        NarsLogger.w("NarsViewModel", "Failed to save naming panel: ${e.message}")
                    }
                }

                showSuccess("Generated ${panels.size} naming panels")
                NarsLogger.d("NarsViewModel", "Generated ${panels.size} naming panels")
            } catch (e: Exception) {
                NarsLogger.e("NarsViewModel", "Failed to generate naming panels", e)
                showError("Failed to generate naming panels")
            }
        }
    }

    /**
     * Set house numbers for reference road
     */
    fun setHouseNumbers() {
        viewModelScope.launch {
            try {
                val roadId = featureStore.referenceRoadDbId.value
                if (roadId == null) {
                    showError("No reference road selected")
                    return@launch
                }

                val road = featureStore.getFeatureById(roadId)
                if (road == null) {
                    showError("Reference road not found")
                    return@launch
                }

                val entrances = featureStore.getFeaturesByPhase("houseEntrances")
                val updated = houseNumberingManager.setHouseNumbers(entrances, road)

                // Update features
                updated.forEach { entrance ->
                    featureStore.updateFeature(entrance.id, entrance)
                    // TODO: Save to backend
                }

                val numberedCount = updated.count { it.properties.entranceNumber != null && it.properties.entranceNumber > 0 }
                showSuccess("Assigned numbers to $numberedCount entrances")
                NarsLogger.d("NarsViewModel", "Numbered $numberedCount entrances")
            } catch (e: Exception) {
                NarsLogger.e("NarsViewModel", "Failed to set house numbers", e)
                showError("Failed to set house numbers")
            }
        }
    }

    /**
     * Undo last action
     */
    fun undo(): Boolean {
        val action = featureStore.executeUndo()
        if (action == null) {
            showError("Nothing to undo")
            return false
        }

        when (action) {
            is UndoAction.Delete -> {
                // Restore deleted feature
                featureStore.addFeature(action.feature)
                showSuccess("Restored: ${action.feature.properties.name}")
            }
            is UndoAction.Update -> {
                // Restore old feature state
                featureStore.updateFeature(action.oldFeature.id, action.oldFeature)
                showSuccess("Restored: ${action.oldFeature.properties.name}")
            }
        }
        return true
    }

    /**
     * Add a new feature
     */
    fun addFeature(feature: NarsFeature) {
        featureStore.addFeature(feature)
    }

    /**
     * Update an existing feature
     */
    fun updateFeature(feature: NarsFeature) {
        // Record for undo (simplified - would track old state properly)
        featureStore.addUndoAction(UndoAction.Update(
            oldFeature = featureStore.getFeatureById(feature.id) ?: feature,
            newFeature = feature,
            phaseKey = feature.properties.phase
        ))
        featureStore.updateFeature(feature.id, feature)
    }

    /**
     * Delete a feature
     */
    fun deleteFeature(featureId: String) {
        val feature = featureStore.getFeatureById(featureId)
        if (feature != null) {
            featureStore.addUndoAction(UndoAction.Delete(
                feature = feature,
                phaseKey = feature.properties.phase
            ))
        }
        featureStore.removeFeature(featureId)
        _selectedFeatureId.value = null
    }

    // Selected feature ID for editing
    private val _selectedFeatureId = MutableStateFlow<String?>(null)
    val selectedFeatureId: StateFlow<String?> = _selectedFeatureId.asStateFlow()

    /**
     * Select a feature for editing
     */
    fun selectFeature(feature: NarsFeature?) {
        featureStore.selectFeature(feature)
        _selectedFeatureId.value = feature?.id
    }

    /**
     * Toggle drawing mode
     */
    fun toggleDrawing(enabled: Boolean) {
        _drawingEnabled.value = enabled
        if (enabled) {
            _editModeEnabled.value = false
        }
    }

    /**
     * Toggle edit mode
     */
    fun toggleEditMode(enabled: Boolean) {
        _editModeEnabled.value = enabled
        if (enabled) {
            _drawingEnabled.value = false
        }
    }

    /**
     * Set base layer
     */
    fun setBaseLayer(layer: BaseLayerType) {
        _baseLayer.value = layer
        appPreferences.baseLayer = layer
    }

    /**
     * Set theme mode
     */
    fun setThemeMode(mode: ThemeMode) {
        _themeMode.value = mode
        appPreferences.themeMode = mode
    }

    /**
     * Clear selection
     */
    fun clearSelection() {
        featureStore.selectFeature(null)
        _selectedFeatureId.value = null
    }

    /**
     * Show loading state
     */
    fun setLoading(loading: Boolean) {
        _uiState.value = _uiState.value.copy(isLoading = loading)
    }

    /**
     * Show error message
     */
    fun showError(message: String) {
        _uiState.value = _uiState.value.copy(errorMessage = message)
    }

    /**
     * Clear error message
     */
    fun clearError() {
        _uiState.value = _uiState.value.copy(errorMessage = null)
    }

    /**
     * Show success message
     */
    fun showSuccess(message: String) {
        _uiState.value = _uiState.value.copy(successMessage = message)
    }

    /**
     * Clear success message
     */
    fun clearSuccess() {
        _uiState.value = _uiState.value.copy(successMessage = null)
    }
}

/**
 * UI state data class
 */
data class UiState(
    val isLoading: Boolean = false,
    val errorMessage: String? = null,
    val successMessage: String? = null,
    val showSettings: Boolean = false,
    val showFeatureModal: Boolean = false
)
