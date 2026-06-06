package com.nars.maplibre.data.store

import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.utils.NarsLogger
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.util.Collections

class FeatureStore {

    private val _featuresByPhase = MutableStateFlow<Map<String, List<NarsFeature>>>(emptyMap())
    val featuresByPhase: StateFlow<Map<String, List<NarsFeature>>> = _featuresByPhase.asStateFlow()

    private val _allFeatures = MutableStateFlow<List<NarsFeature>>(emptyList())
    val allFeatures: StateFlow<List<NarsFeature>> = _allFeatures.asStateFlow()

    private val _selectedFeature = MutableStateFlow<NarsFeature?>(null)
    val selectedFeature: StateFlow<NarsFeature?> = _selectedFeature.asStateFlow()

    private val _currentPhase = MutableStateFlow<PhaseDefinition?>(null)
    val currentPhase: StateFlow<PhaseDefinition?> = _currentPhase.asStateFlow()

    private val _featureCounts = MutableStateFlow(FeatureCounts())
    val featureCounts: StateFlow<FeatureCounts> = _featureCounts.asStateFlow()

    private val _referenceRoadDbId = MutableStateFlow<String?>(null)
    val referenceRoadDbId: StateFlow<String?> = _referenceRoadDbId.asStateFlow()

    private val _undoStack = Collections.synchronizedList(mutableListOf<UndoAction>())
    val canUndo: Boolean get() = _undoStack.isNotEmpty()

    init {
        _currentPhase.value = Phases.ALL.first()
    }

    fun setCurrentPhase(phase: PhaseDefinition) {
        _currentPhase.value = phase
    }

    fun setCurrentPhaseByKey(key: String) {
        Phases.getByKey(key)?.let { setCurrentPhase(it) }
    }

    fun addFeature(feature: NarsFeature, recordUndo: Boolean = false) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        val phaseFeatures = currentMap.getOrPut(feature.properties.phase) { emptyList() }
        currentMap[feature.properties.phase] = phaseFeatures + feature
        _featuresByPhase.value = currentMap

        _allFeatures.value = _allFeatures.value + feature

        if (recordUndo) {
            addUndoAction(UndoAction.Create(feature, feature.properties.phase))
        }
    }

    fun addFeatures(features: List<NarsFeature>) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        features.forEach { feature ->
            val phaseFeatures = currentMap.getOrPut(feature.properties.phase) { emptyList() }
            currentMap[feature.properties.phase] = phaseFeatures + feature
        }
        _featuresByPhase.value = currentMap
        _allFeatures.value = _allFeatures.value + features
    }

    fun updateFeature(featureId: String, updatedFeature: NarsFeature) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        val updatedMap = mutableMapOf<String, List<NarsFeature>>()
        currentMap.forEach { (phase, features) ->
            updatedMap[phase] = features.map { if (it.id == featureId) updatedFeature else it }
        }
        _featuresByPhase.value = updatedMap
        _allFeatures.value = _allFeatures.value.map { if (it.id == featureId) updatedFeature else it }
        if (_selectedFeature.value?.id == featureId) {
            _selectedFeature.value = updatedFeature
        }
    }

    fun removeFeature(featureId: String) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        currentMap.forEach { (phase, features) ->
            val filtered = features.filter { it.id != featureId }
            if (filtered.isEmpty()) {
                currentMap.remove(phase)
            } else {
                currentMap[phase] = filtered
            }
        }
        _featuresByPhase.value = currentMap
        _allFeatures.value = _allFeatures.value.filter { it.id != featureId }

        if (_selectedFeature.value?.id == featureId) {
            _selectedFeature.value = null
        }
    }

    fun getFeaturesByPhase(phaseKey: String): List<NarsFeature> {
        return _featuresByPhase.value[phaseKey] ?: emptyList()
    }

    fun getCurrentPhaseFeatures(): List<NarsFeature> {
        return currentPhase.value?.let { getFeaturesByPhase(it.key) } ?: emptyList()
    }

    fun getFeatureById(featureId: String): NarsFeature? {
        return _allFeatures.value.find { it.id == featureId }
    }

    fun selectFeature(feature: NarsFeature?) {
        _selectedFeature.value = feature
    }

    fun clearAll() {
        _featuresByPhase.value = emptyMap()
        _allFeatures.value = emptyList()
        _selectedFeature.value = null
    }

    fun clearPhase(phaseKey: String) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        currentMap.remove(phaseKey)
        _featuresByPhase.value = currentMap
        _allFeatures.value = _allFeatures.value.filter { it.properties.phase != phaseKey }
    }

    fun getFeatureCounts(): Map<String, Int> {
        return _featuresByPhase.value.mapValues { it.value.size }
    }

    fun setReferenceRoad(dbId: String?) {
        _referenceRoadDbId.value = dbId
    }

    fun addUndoAction(action: UndoAction) {
        _undoStack.add(action)
        if (_undoStack.size > 50) {
            _undoStack.removeAt(0)
        }
    }

    fun popUndoAction(): UndoAction? {
        if (_undoStack.isEmpty()) return null
        return _undoStack.removeAt(_undoStack.lastIndex)
    }

    fun executeUndo(): UndoAction? {
        val action = popUndoAction() ?: return null

        if (action is UndoAction.Delete) {
            val feature = action.feature
            if (feature.properties.entranceTypeKey == "main_entrance") {
                addFeature(feature)
            }
            val roadDbId = feature.properties.roadDbId
            if (roadDbId != null) {
                val roadPhase = _featuresByPhase.value["roads"] ?: emptyList()
                val road = roadPhase.find { it.id == roadDbId || it.dbId == roadDbId }
                if (road != null) {
                    NarsLogger.d("FeatureStore", "Repaired cross-reference: restored entrance for road ${road.properties.name}")
                }
            }
        }

        return action
    }

    fun getAllRoads(): List<NarsFeature> {
        return _featuresByPhase.value["roads"] ?: emptyList()
    }
}

data class FeatureCounts(
    val roads: Int = 0,
    val mainEntrances: Int = 0,
    val secondaryEntrances: Int = 0,
    val namingPanels: Int = 0
)
