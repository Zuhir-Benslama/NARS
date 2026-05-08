package com.nars.maplibre.data.repository

import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.store.FeatureStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Local implementation of FeatureRepository using FeatureStore
 * In production, this would integrate with a database or API
 */
class LocalFeatureRepository(
    private val featureStore: FeatureStore
) : FeatureRepository {
    
    override fun getAllFeatures(): Flow<List<NarsFeature>> {
        return featureStore.allFeatures
    }
    
    override fun getFeaturesByPhase(phaseKey: String): Flow<List<NarsFeature>> {
        return featureStore.featuresByPhase.map { it[phaseKey] ?: emptyList() }
    }
    
    override suspend fun getFeatureById(featureId: String): NarsFeature? = withContext(Dispatchers.Default) {
        featureStore.getFeatureById(featureId)
    }
    
    override suspend fun saveFeature(feature: NarsFeature): Result<NarsFeature> = withContext(Dispatchers.Default) {
        try {
            featureStore.addFeature(feature)
            Result.success(feature)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
    
    override suspend fun updateFeature(feature: NarsFeature): Result<Unit> = withContext(Dispatchers.Default) {
        try {
            featureStore.updateFeature(feature.id, feature)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
    
    override suspend fun deleteFeature(featureId: String): Result<Unit> = withContext(Dispatchers.Default) {
        try {
            featureStore.removeFeature(featureId)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
    
    override suspend fun deleteFeaturesByPhase(phaseKey: String): Result<Unit> = withContext(Dispatchers.Default) {
        try {
            featureStore.clearPhase(phaseKey)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
    
    override suspend fun importFeatures(geoJson: String): Result<List<NarsFeature>> = withContext(Dispatchers.Default) {
        // TODO: Implement GeoJSON parsing
        Result.failure(NotImplementedError("GeoJSON import not implemented"))
    }
    
    override suspend fun exportFeatures(): Result<String> = withContext(Dispatchers.Default) {
        // TODO: Implement GeoJSON export
        Result.failure(NotImplementedError("GeoJSON export not implemented"))
    }
}
