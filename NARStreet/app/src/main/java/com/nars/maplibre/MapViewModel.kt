package com.nars.maplibre

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.data.model.BaseLayerType
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.store.FeatureStore
import com.nars.maplibre.data.store.UndoAction
import com.nars.maplibre.domain.ComputeRoadDirectionsUseCase
import com.nars.maplibre.domain.GenerateNamingPanelsUseCase
import com.nars.maplibre.domain.SetHouseNumbersUseCase
import com.nars.maplibre.utils.NarsLogger
import com.nars.maplibre.utils.PhaseNavigator
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

class MapViewModel(
    val featureStore: FeatureStore,
    private val appPreferences: AppPreferences,
    private val apiService: ApiService,
    private val computeRoadDirectionsUseCase: ComputeRoadDirectionsUseCase,
    private val generateNamingPanelsUseCase: GenerateNamingPanelsUseCase,
    private val setHouseNumbersUseCase: SetHouseNumbersUseCase
) : ViewModel() {

    private val phaseNavigator = PhaseNavigator(featureStore)

    val currentPhase: StateFlow<PhaseDefinition?> = featureStore.currentPhase
    val allFeatures: StateFlow<List<NarsFeature>> = featureStore.allFeatures
    private val _currentPhaseFeatures = MutableStateFlow<List<NarsFeature>>(emptyList())
    val currentPhaseFeatures: StateFlow<List<NarsFeature>> = _currentPhaseFeatures
    val selectedFeature: StateFlow<NarsFeature?> = featureStore.selectedFeature

    private val _uiState = MutableStateFlow(UiState())
    val uiState: StateFlow<UiState> = _uiState.asStateFlow()

    private val _baseLayer = MutableStateFlow(appPreferences.baseLayer)
    val baseLayer: StateFlow<BaseLayerType> = _baseLayer.asStateFlow()

    private val _drawingEnabled = MutableStateFlow(false)
    val drawingEnabled: StateFlow<Boolean> = _drawingEnabled.asStateFlow()

    private val _editModeEnabled = MutableStateFlow(false)
    val editModeEnabled: StateFlow<Boolean> = _editModeEnabled.asStateFlow()

    val referenceRoadDbId: StateFlow<String?> = featureStore.referenceRoadDbId
    val referenceEntranceDbId: StateFlow<String?> = featureStore.referenceEntranceDbId
    val canUndo: Boolean get() = featureStore.canUndo

    init {
        featureStore.setCurrentPhase(Phases.ALL.first())
        viewModelScope.launch {
            featureStore.currentPhase.collect { phase ->
                phase?.let {
                    _currentPhaseFeatures.value = featureStore.getFeaturesByPhase(it.key)
                }
            }
        }
    }

    fun setCurrentPhase(phase: PhaseDefinition): PhaseDefinition? {
        val currentIndex = featureStore.currentPhase.value?.index ?: 0
        if (phase.index > currentIndex) {
            val error = phaseNavigator.canAdvance(phase.index)
            if (error != null) {
                showError(error)
                NarsLogger.d("MapViewModel", "Phase validation failed: $error")
                return null
            }
            if (featureStore.currentPhase.value?.key == "roads" && phase.key != "roads") {
                computeRoadDirections()
            }
        }
        NarsLogger.d("MapViewModel", "Setting current phase to: ${phase.label} (${phase.key})")
        featureStore.setCurrentPhase(phase)
        appPreferences.currentPhase = phase.key
        _drawingEnabled.value = false
        _editModeEnabled.value = false
        return phase
    }

    fun goToNextPhase(): PhaseDefinition? {
        val nextPhase = phaseNavigator.goNext()
        if (nextPhase == null) {
            val currentIndex = featureStore.currentPhase.value?.index ?: 0
            val error = phaseNavigator.canAdvance(currentIndex + 1)
            if (error != null) showError(error)
        }
        return nextPhase
    }

    fun goToPreviousPhase(): PhaseDefinition? = phaseNavigator.goBack()
    fun canGoNextPhase(): Boolean = phaseNavigator.canGoForward()

    fun setReferenceRoad(dbId: String?) = featureStore.setReferenceRoad(dbId)
    fun setReferenceEntrance(dbId: String?) = featureStore.setReferenceEntrance(dbId)

    fun computeRoadDirections() {
        viewModelScope.launch {
            computeRoadDirectionsUseCase()
                .onSuccess { msg -> showSuccess(msg); NarsLogger.d("MapViewModel", "Road directions: $msg") }
                .onFailure { e -> NarsLogger.e("MapViewModel", "Failed to compute road directions", e); showError("Failed to compute road directions") }
        }
    }

    fun generateNamingPanels() {
        viewModelScope.launch {
            generateNamingPanelsUseCase()
                .onSuccess { count -> showSuccess("Generated $count naming panels"); NarsLogger.d("MapViewModel", "Generated $count naming panels") }
                .onFailure { e -> NarsLogger.e("MapViewModel", "Failed to generate naming panels", e); showError("Failed to generate naming panels") }
        }
    }

    fun setHouseNumbers() {
        viewModelScope.launch {
            setHouseNumbersUseCase()
                .onSuccess { count -> showSuccess("Assigned numbers to $count entrances"); NarsLogger.d("MapViewModel", "Numbered $count entrances") }
                .onFailure { e -> showError(e.message ?: "Failed to set house numbers"); NarsLogger.e("MapViewModel", "Failed to set house numbers", e) }
        }
    }

    fun undo(): Boolean {
        val action = featureStore.executeUndo()
        if (action == null) {
            showError("Nothing to undo")
            return false
        }
        when (action) {
            is UndoAction.Delete -> {
                // FeatureStore.executeUndo already re-added main entrances (cross-reference repair)
                showSuccess("Restored: ${action.feature.properties.name}")
            }
            is UndoAction.Create -> {
                featureStore.removeFeature(action.feature.id)
                showSuccess("Removed: ${action.feature.properties.name}")
            }
            is UndoAction.Update -> {
                featureStore.updateFeature(action.oldFeature.id, action.oldFeature)
                showSuccess("Restored: ${action.oldFeature.properties.name}")
            }
        }
        return true
    }

    fun addFeature(feature: NarsFeature) = featureStore.addFeature(feature, recordUndo = true)

    fun updateFeature(feature: NarsFeature) {
        val oldFeature = featureStore.getFeatureById(feature.id)
        featureStore.updateFeature(feature.id, feature)
        oldFeature?.let {
            featureStore.addUndoAction(UndoAction.Update(
                oldFeature = it,
                newFeature = feature,
                phaseKey = feature.properties.phase
            ))
        }
    }

    fun deleteFeature(featureId: String) {
        val feature = featureStore.getFeatureById(featureId)
        if (feature != null) {
            featureStore.addUndoAction(UndoAction.Delete(
                feature = feature,
                phaseKey = feature.properties.phase
            ))
        }
        featureStore.removeFeature(featureId)
    }

    val selectedFeatureId: StateFlow<String?> = featureStore.selectedFeature.map { it?.id }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(), null)

    fun selectFeature(feature: NarsFeature?) = featureStore.selectFeature(feature)

    fun toggleDrawing(enabled: Boolean) {
        _drawingEnabled.value = enabled
        if (enabled) _editModeEnabled.value = false
    }

    fun toggleEditMode(enabled: Boolean) {
        _editModeEnabled.value = enabled
        if (enabled) _drawingEnabled.value = false
    }

    fun setBaseLayer(layer: BaseLayerType) {
        _baseLayer.value = layer
        appPreferences.baseLayer = layer
    }

    fun clearSelection() = featureStore.selectFeature(null)

    fun setLoading(loading: Boolean) {
        _uiState.value = _uiState.value.copy(isLoading = loading)
    }

    fun showError(message: String) {
        _uiState.value = _uiState.value.copy(errorMessage = message)
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(errorMessage = null)
    }

    fun showSuccess(message: String) {
        _uiState.value = _uiState.value.copy(successMessage = message)
    }

    fun clearSuccess() {
        _uiState.value = _uiState.value.copy(successMessage = null)
    }
}

data class UiState(
    val isLoading: Boolean = false,
    val errorMessage: String? = null,
    val successMessage: String? = null,
    val showSettings: Boolean = false,
    val showFeatureModal: Boolean = false
)
