package com.nars.maplibre.domain

import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.store.FeatureStore
import com.nars.maplibre.utils.RoadDirectionsCalculator

class ComputeRoadDirectionsUseCase(
    private val featureStore: FeatureStore,
    private val roadDirectionsCalculator: RoadDirectionsCalculator = RoadDirectionsCalculator()
) {
    suspend operator fun invoke(): Result<String> {
        return try {
            val roads = featureStore.getFeaturesByPhase("roads")
            val result = roadDirectionsCalculator.computeDirectionsFromRoads(roads)
            for (roadId in result.reversedRoadIds) {
                val road = featureStore.getFeatureById(roadId) ?: continue
                val geom = road.geometry as? LineStringGeometry ?: continue
                val reversed = roadDirectionsCalculator.reverseRoadCoordinates(geom.coordinates)
                featureStore.updateFeature(road.id, road.copy(
                    geometry = LineStringGeometry(type = "LineString", coordinates = reversed)
                ))
            }
            Result.success(result.message)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
