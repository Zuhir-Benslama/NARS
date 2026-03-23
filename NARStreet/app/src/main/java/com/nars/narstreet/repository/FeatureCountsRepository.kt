package com.nars.narstreet.repository

import com.nars.narstreet.data.model.CityCenterEntity
import com.nars.narstreet.data.model.EntranceEntity
import com.nars.narstreet.ui.components.NarsFeatureCounts
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.combine
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Aggregates live feature counts from all phase repositories into a single Flow.
 * Uses the combine(flows: Iterable) overload which handles any number of flows,
 * avoiding the 5-argument limit on the positional combine() variants.
 */
@Singleton
class FeatureCountsRepository @Inject constructor(
    private val areaRepo: AreaRepository,
    private val districtRepo: DistrictRepository,
    private val cityCenterRepo: CityCenterRepository,
    private val roadRepo: RoadRepository,
    private val entranceRepo: EntranceRepository,
    private val buildingRepo: BuildingRepository,
    private val spaceRepo: SpaceRepository,
) {
    val counts: Flow<NarsFeatureCounts> = combine(
        listOf(
            areaRepo.areas,
            districtRepo.districts,
            cityCenterRepo.cityCenter,
            roadRepo.roads,
            entranceRepo.entrances,
            buildingRepo.buildings,
            spaceRepo.spaces,
        )
    ) { values ->
        @Suppress("UNCHECKED_CAST")
        val areas      = values[0] as List<*>
        val districts  = values[1] as List<*>
        val cityCenter = values[2]          // CityCenterEntity? — nullable singleton
        val roads      = values[3] as List<*>
        // filterIsInstance avoids the unchecked cast warning while remaining safe
        val entrances  = (values[4] as List<*>).filterIsInstance<EntranceEntity>()
        val buildings  = values[5] as List<*>
        val spaces     = values[6] as List<*>

        NarsFeatureCounts(
            areas         = areas.size,
            districts     = districts.size,
            cityCenter    = if (cityCenter != null) 1 else 0,
            roads         = roads.size,
            mainEntrances = entrances.count { it.entranceTypeKey == "main_entrance" },
            secEntrances  = entrances.count { it.entranceTypeKey == "secondary_entrance" },
            buildings     = buildings.size,
            spaces        = spaces.size,
        )
    }
}
