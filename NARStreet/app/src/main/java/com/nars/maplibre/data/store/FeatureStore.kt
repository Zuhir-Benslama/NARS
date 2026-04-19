package com.nars.maplibre.data.store

import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Feature store managing all NARS features
 * Similar to featuresStore in web version
 */
class FeatureStore {

    // All features organized by phase
    private val _featuresByPhase = MutableStateFlow<Map<String, List<NarsFeature>>>(emptyMap())
    val featuresByPhase: StateFlow<Map<String, List<NarsFeature>>> = _featuresByPhase.asStateFlow()

    // All features as a flat list
    private val _allFeatures = MutableStateFlow<List<NarsFeature>>(emptyList())
    val allFeatures: StateFlow<List<NarsFeature>> = _allFeatures.asStateFlow()

    // Currently selected feature
    private val _selectedFeature = MutableStateFlow<NarsFeature?>(null)
    val selectedFeature: StateFlow<NarsFeature?> = _selectedFeature.asStateFlow()

    // Currently active phase
    private val _currentPhase = MutableStateFlow<PhaseDefinition?>(null)
    val currentPhase: StateFlow<PhaseDefinition?> = _currentPhase.asStateFlow()

    // Feature counts (similar to web version's FeatureCounts)
    private val _featureCounts = MutableStateFlow(FeatureCounts())
    val featureCounts: StateFlow<FeatureCounts> = _featureCounts.asStateFlow()

    // Reference road for house entrance placement
    private val _referenceRoadDbId = MutableStateFlow<String?>(null)
    val referenceRoadDbId: StateFlow<String?> = _referenceRoadDbId.asStateFlow()

    // Reference entrance for secondary entrance placement
    private val _referenceEntranceDbId = MutableStateFlow<String?>(null)
    val referenceEntranceDbId: StateFlow<String?> = _referenceEntranceDbId.asStateFlow()

    // Undo stack
    private val _undoStack = mutableListOf<UndoAction>()
    val canUndo: Boolean get() = _undoStack.isNotEmpty()

    init {
        // Start with first phase
        _currentPhase.value = Phases.ALL.first()
    }

    /**
     * Sync feature counts from current state
     * Similar to web version's syncCounts()
     */
    fun syncCounts() {
        val features = _featuresByPhase.value
        _featureCounts.value = FeatureCounts(
            areas = features["areas"]?.size ?: 0,
            cityCenter = features["cityCenter"]?.size ?: 0,
            districts = features["districts"]?.size ?: 0,
            roads = features["roads"]?.size ?: 0,
            mainEntrances = features["houseEntrances"]?.count { it.properties.entranceTypeKey == "main_entrance" } ?: 0,
            secondaryEntrances = features["houseEntrances"]?.count { it.properties.entranceTypeKey == "secondary_entrance" } ?: 0,
            publicBuildings = features["publicBuildings"]?.size ?: 0,
            publicSpaces = features["publicSpaces"]?.size ?: 0,
            namingPanels = features["namingPanels"]?.size ?: 0
        )
    }
    
    /**
     * Set current phase
     */
    fun setCurrentPhase(phase: PhaseDefinition) {
        _currentPhase.value = phase
    }
    
    fun setCurrentPhaseByKey(key: String) {
        Phases.getByKey(key)?.let { setCurrentPhase(it) }
    }
    
    /**
     * Add a feature
     */
    fun addFeature(feature: NarsFeature) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        val phaseFeatures = currentMap.getOrPut(feature.properties.phase) { emptyList() }
        currentMap[feature.properties.phase] = phaseFeatures + feature
        _featuresByPhase.value = currentMap
        
        // Update all features
        _allFeatures.value = _allFeatures.value + feature
    }
    
    /**
     * Add multiple features (batch)
     */
    fun addFeatures(features: List<NarsFeature>) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        features.forEach { feature ->
            val phaseFeatures = currentMap.getOrPut(feature.properties.phase) { emptyList() }
            currentMap[feature.properties.phase] = phaseFeatures + feature
        }
        _featuresByPhase.value = currentMap
        _allFeatures.value = _allFeatures.value + features
    }
    
    /**
     * Update a feature
     */
    fun updateFeature(featureId: String, updatedFeature: NarsFeature) {
        removeFeature(featureId)
        addFeature(updatedFeature)
    }
    
    /**
     * Remove a feature
     */
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
        
        // Clear selection if removed feature was selected
        if (_selectedFeature.value?.id == featureId) {
            _selectedFeature.value = null
        }
    }
    
    /**
     * Get features by phase
     */
    fun getFeaturesByPhase(phaseKey: String): List<NarsFeature> {
        return _featuresByPhase.value[phaseKey] ?: emptyList()
    }
    
    /**
     * Get features for current phase
     */
    fun getCurrentPhaseFeatures(): List<NarsFeature> {
        return currentPhase.value?.let { getFeaturesByPhase(it.key) } ?: emptyList()
    }
    
    /**
     * Get feature by ID
     */
    fun getFeatureById(featureId: String): NarsFeature? {
        return _allFeatures.value.find { it.id == featureId }
    }
    
    /**
     * Select a feature
     */
    fun selectFeature(feature: NarsFeature?) {
        _selectedFeature.value = feature
    }
    
    /**
     * Clear all features
     */
    fun clearAll() {
        _featuresByPhase.value = emptyMap()
        _allFeatures.value = emptyList()
        _selectedFeature.value = null
    }
    
    /**
     * Clear features for a specific phase
     */
    fun clearPhase(phaseKey: String) {
        val currentMap = _featuresByPhase.value.toMutableMap()
        currentMap.remove(phaseKey)
        _featuresByPhase.value = currentMap
        _allFeatures.value = _allFeatures.value.filter { it.properties.phase != phaseKey }
    }
    
    /**
     * Get feature count by phase
     */
    fun getFeatureCounts(): Map<String, Int> {
        return _featuresByPhase.value.mapValues { it.value.size }
    }

    // ─── Reference State ───────────────────────────────────────────────────────

    fun setReferenceRoad(dbId: String?) {
        _referenceRoadDbId.value = dbId
    }

    fun setReferenceEntrance(dbId: String?) {
        _referenceEntranceDbId.value = dbId
    }

    // ─── Undo System ───────────────────────────────────────────────────────────

    fun addUndoAction(action: UndoAction) {
        _undoStack.add(action)
        // Limit stack size to 50
        if (_undoStack.size > 50) {
            _undoStack.removeAt(0)
        }
    }

    /**
     * Pop and return the last undo action without executing it
     * Caller is responsible for applying the undo
     */
    fun popUndoAction(): UndoAction? {
        if (_undoStack.isEmpty()) return null
        return _undoStack.removeLast()
    }

    /**
     * Execute undo - returns the action that should be reversed
     * Callers should then reverse the action (e.g., re-add deleted feature)
     */
    fun executeUndo(): UndoAction? {
        return popUndoAction()
    }

    /**
     * Get all roads for reference selection
     */
    fun getAllRoads(): List<NarsFeature> {
        return _featuresByPhase.value["roads"] ?: emptyList()
    }

    /**
     * Get all main entrances for reference selection
     */
    fun getAllMainEntrances(): List<NarsFeature> {
        return (_featuresByPhase.value["houseEntrances"] ?: emptyList())
            .filter { it.properties.entranceTypeKey == "main_entrance" }
    }
}

/**
 * Feature counts data class matching web version (types.ts: FeatureCounts)
 */
data class FeatureCounts(
    val areas: Int = 0,
    val cityCenter: Int = 0,
    val districts: Int = 0,
    val roads: Int = 0,
    val mainEntrances: Int = 0,
    val secondaryEntrances: Int = 0,
    val publicBuildings: Int = 0,
    val publicSpaces: Int = 0,
    val namingPanels: Int = 0
)
