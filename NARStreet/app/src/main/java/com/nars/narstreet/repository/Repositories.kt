package com.nars.narstreet.repository

import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.util.toAnyDouble
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncScheduler
import com.nars.narstreet.core.util.extractCoords
import com.nars.narstreet.data.dao.BuildingDao
import com.nars.narstreet.data.dao.PanelDao
import com.nars.narstreet.data.dao.SpaceDao
import com.nars.narstreet.data.model.*
import kotlinx.coroutines.flow.Flow
import javax.inject.Inject
import javax.inject.Singleton

// ── BuildingRepository ────────────────────────────────────────────────────────

@Singleton
class BuildingRepository @Inject constructor(
    private val dao: BuildingDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val syncScheduler: SyncScheduler,
) {
    val buildings: Flow<List<BuildingEntity>> = dao.getAll()
    val pendingCount: Flow<Int>               = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadByType("public_building")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                val data     = f.data.parse()
                BuildingEntity(
                    id         = existing?.id ?: 0,
                    remoteId   = f.id,
                    label      = f.label,
                    layer      = f.layer,
                    lat        = data["lat"].toAnyDouble(),
                    lng        = data["lng"].toAnyDouble(),
                    dataJson   = f.data.json,
                    syncStatus = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (e: Exception) {
            android.util.Log.e("NARStreet", "BuildingRepository.refresh failed: ${e.message}", e)
        }
    }

    suspend fun saveGeometry(building: BuildingEntity) {
        dao.update(building.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
    }
}

// ── SpaceRepository ───────────────────────────────────────────────────────────

@Singleton
class SpaceRepository @Inject constructor(
    private val dao: SpaceDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val syncScheduler: SyncScheduler,
) {
    val spaces: Flow<List<SpaceEntity>> = dao.getAll()
    val pendingCount: Flow<Int>         = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadByType("public_space")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                SpaceEntity(
                    id              = existing?.id ?: 0,
                    remoteId        = f.id,
                    label           = f.label,
                    layer           = f.layer,
                    coordinatesJson = extractCoords(f.data.parse()),
                    dataJson        = f.data.json,
                    syncStatus      = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (e: Exception) {
            android.util.Log.e("NARStreet", "SpaceRepository.refresh failed: ${e.message}", e)
        }
    }

    suspend fun saveGeometry(space: SpaceEntity) {
        dao.update(space.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
    }
}

// ── PanelRepository ───────────────────────────────────────────────────────────

@Singleton
class PanelRepository @Inject constructor(
    private val dao: PanelDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val syncScheduler: SyncScheduler,
) {
    val panels: Flow<List<PanelEntity>> = dao.getAll()
    val pendingCount: Flow<Int>         = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadLayer("naming_panel")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                PanelEntity(
                    id                = existing?.id ?: 0,
                    remoteId          = f.id,
                    label             = f.label,
                    lat               = f.data.parse()["lat"].toAnyDouble(),
                    lng               = f.data.parse()["lng"].toAnyDouble(),
                    dataJson          = f.data.json,
                    isPlaced          = existing?.isPlaced,
                    isCorrectLocation = existing?.isCorrectLocation,
                    syncStatus        = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (e: Exception) {
            android.util.Log.e("NARStreet", "PanelRepository.refresh failed: ${e.message}", e)
        }
    }

    suspend fun saveChecklist(panel: PanelEntity) {
        dao.update(panel.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
    }
}
