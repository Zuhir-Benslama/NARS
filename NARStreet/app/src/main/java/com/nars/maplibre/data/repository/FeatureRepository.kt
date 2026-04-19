package com.nars.maplibre.data.repository

import com.nars.maplibre.data.model.NarsFeature
import kotlinx.coroutines.flow.Flow

/**
 * Repository interface for feature data operations
 */
interface FeatureRepository {
    
    /**
     * Get all features as a Flow
     */
    fun getAllFeatures(): Flow<List<NarsFeature>>
    
    /**
     * Get features by phase
     */
    fun getFeaturesByPhase(phaseKey: String): Flow<List<NarsFeature>>
    
    /**
     * Get a single feature by ID
     */
    suspend fun getFeatureById(featureId: String): NarsFeature?
    
    /**
     * Save a new feature
     */
    suspend fun saveFeature(feature: NarsFeature): Result<NarsFeature>
    
    /**
     * Update an existing feature
     */
    suspend fun updateFeature(feature: NarsFeature): Result<Unit>
    
    /**
     * Delete a feature
     */
    suspend fun deleteFeature(featureId: String): Result<Unit>
    
    /**
     * Delete all features for a phase
     */
    suspend fun deleteFeaturesByPhase(phaseKey: String): Result<Unit>
    
    /**
     * Import features from GeoJSON
     */
    suspend fun importFeatures(geoJson: String): Result<List<NarsFeature>>
    
    /**
     * Export features to GeoJSON
     */
    suspend fun exportFeatures(): Result<String>
}
