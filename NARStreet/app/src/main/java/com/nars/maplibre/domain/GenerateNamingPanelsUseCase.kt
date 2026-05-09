package com.nars.maplibre.domain

import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.data.store.FeatureStore
import com.nars.maplibre.utils.NamingPanelGenerator

class GenerateNamingPanelsUseCase(
    private val featureStore: FeatureStore,
    private val apiService: ApiService,
    private val namingPanelGenerator: NamingPanelGenerator = NamingPanelGenerator()
) {
    suspend operator fun invoke(): Result<Int> {
        return try {
            val roads = featureStore.getFeaturesByPhase("roads")
            val panels = namingPanelGenerator.generatePanelsFromRoads(roads)
            panels.forEach { panel ->
                featureStore.addFeature(panel)
                apiService.saveFeature(panel)
            }
            Result.success(panels.size)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
