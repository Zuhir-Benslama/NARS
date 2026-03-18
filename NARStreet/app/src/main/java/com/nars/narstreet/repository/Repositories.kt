package com.nars.narstreet.repository

import androidx.work.*
import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncWorker
import com.nars.narstreet.data.dao.BuildingDao
import com.nars.narstreet.data.dao.PanelDao
import com.nars.narstreet.data.dao.SpaceDao
import com.nars.narstreet.data.model.*
import kotlinx.coroutines.flow.Flow
import java.util.concurrent.TimeUnit
import javax.inject.Inject
import javax.inject.Singleton

// ── BuildingRepository ────────────────────────────────────────────────────────

@Singleton
class BuildingRepository @Inject constructor(
    private val dao: BuildingDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val workManager: WorkManager,
) {
    val buildings: Flow<List<BuildingEntity>> = dao.getAll()
    val pendingCount: Flow<Int>               = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadLayer("public_building")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                BuildingEntity(
                    id              = existing?.id ?: 0,
                    remoteId        = f.id,
                    label           = f.label,
                    layer           = f.layer,
                    coordinatesJson = existing?.coordinatesJson
                        ?: (f.data["coordinates"]?.toString() ?: "[]"),
                    dataJson        = f.data.toString(),
                    syncStatus      = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (_: Exception) { }
    }

    suspend fun saveGeometry(building: BuildingEntity) {
        dao.update(building.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) enqueue()
    }

    private fun enqueue() = workManager.enqueueUniqueWork(
        SyncWorker.TAG, ExistingWorkPolicy.KEEP,
        OneTimeWorkRequestBuilder<SyncWorker>()
            .setConstraints(Constraints(NetworkType.CONNECTED))
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.SECONDS)
            .build(),
    )
}

// ── SpaceRepository ───────────────────────────────────────────────────────────

@Singleton
class SpaceRepository @Inject constructor(
    private val dao: SpaceDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val workManager: WorkManager,
) {
    val spaces: Flow<List<SpaceEntity>> = dao.getAll()
    val pendingCount: Flow<Int>         = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadLayer("garden") + api.loadLayer("square")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                SpaceEntity(
                    id              = existing?.id ?: 0,
                    remoteId        = f.id,
                    label           = f.label,
                    layer           = f.layer,
                    coordinatesJson = existing?.coordinatesJson
                        ?: (f.data["coordinates"]?.toString() ?: "[]"),
                    dataJson        = f.data.toString(),
                    syncStatus      = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (_: Exception) { }
    }

    suspend fun saveGeometry(space: SpaceEntity) {
        dao.update(space.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) enqueue()
    }

    private fun enqueue() = workManager.enqueueUniqueWork(
        SyncWorker.TAG, ExistingWorkPolicy.KEEP,
        OneTimeWorkRequestBuilder<SyncWorker>()
            .setConstraints(Constraints(NetworkType.CONNECTED))
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.SECONDS)
            .build(),
    )
}

// ── PanelRepository ───────────────────────────────────────────────────────────

@Singleton
class PanelRepository @Inject constructor(
    private val dao: PanelDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val workManager: WorkManager,
) {
    val panels: Flow<List<PanelEntity>> = dao.getAll()
    val pendingCount: Flow<Int>         = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadLayer("naming_panel")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                PanelEntity(
                    id              = existing?.id ?: 0,
                    remoteId        = f.id,
                    label           = f.label,
                    lat             = (f.data["lat"] as? Double) ?: 0.0,
                    lng             = (f.data["lng"] as? Double) ?: 0.0,
                    dataJson        = f.data.toString(),
                    isPlaced        = existing?.isPlaced,
                    isCorrectLocation = existing?.isCorrectLocation,
                    syncStatus      = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (_: Exception) { }
    }

    suspend fun saveChecklist(panel: PanelEntity) {
        dao.update(panel.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) enqueue()
    }

    private fun enqueue() = workManager.enqueueUniqueWork(
        SyncWorker.TAG, ExistingWorkPolicy.KEEP,
        OneTimeWorkRequestBuilder<SyncWorker>()
            .setConstraints(Constraints(NetworkType.CONNECTED))
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.SECONDS)
            .build(),
    )
}
