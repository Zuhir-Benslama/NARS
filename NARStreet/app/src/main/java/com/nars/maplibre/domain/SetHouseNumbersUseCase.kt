package com.nars.maplibre.domain

import com.nars.maplibre.data.store.FeatureStore
import com.nars.maplibre.utils.HouseNumberingManager

class SetHouseNumbersUseCase(
    private val featureStore: FeatureStore,
    private val houseNumberingManager: HouseNumberingManager = HouseNumberingManager()
) {
    suspend operator fun invoke(): Result<Int> {
        val roadId = featureStore.referenceRoadDbId.value
            ?: return Result.failure(Exception("No reference road selected"))
        val road = featureStore.getFeatureById(roadId)
            ?: return Result.failure(Exception("Reference road not found"))
        val entrances = featureStore.getFeaturesByPhase("houseEntrances")
        val updated = houseNumberingManager.setHouseNumbers(entrances, road)
        updated.forEach { entrance -> featureStore.updateFeature(entrance.id, entrance) }
        val count = updated.count { it.properties.entranceNumber != null && it.properties.entranceNumber > 0 }
        return Result.success(count)
    }
}
