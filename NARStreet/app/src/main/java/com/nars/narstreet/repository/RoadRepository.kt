package com.nars.narstreet.repository

import androidx.work.*
import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncWorker
import com.nars.narstreet.data.dao.RoadDao
import com.nars.narstreet.data.model.RoadEntity
import com.nars.narstreet.data.model.SyncStatus
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.first
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class RoadRepository @Inject constructor(
    private val dao: RoadDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val workManager: WorkManager,
) {
    val roads: Flow<List<RoadEntity>> = dao.getAll()
    val pendingCount: Flow<Int>       = dao.pendingCount()

    /** Pull all roads from the server and cache them locally. */
    suspend fun refresh() {
        try {
            val features = api.loadLayer("road")
            val entities = features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                RoadEntity(
                    id           = existing?.id ?: 0,
                    remoteId     = f.id,
                    label        = f.label,
                    dataJson     = f.data.toString(),
                    // preserve any locally-entered characteristics
                    lanes          = existing?.lanes,
                    trafficCapacity= existing?.trafficCapacity,
                    tradActivity   = existing?.tradActivity,
                    hasMedianStrip = existing?.hasMedianStrip,
                    hasGreenery    = existing?.hasGreenery,
                    isDeadEnd      = existing?.isDeadEnd,
                    syncStatus     = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            }
            dao.insertAll(entities)
        } catch (_: Exception) { /* offline — use cached data */ }
    }

    /** Save road characteristics locally, schedule sync if online. */
    suspend fun saveCharacteristics(road: RoadEntity) {
        dao.update(road.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) enqueueSyncWork()
    }

    private fun enqueueSyncWork() {
        workManager.enqueueUniqueWork(
            SyncWorker.TAG,
            ExistingWorkPolicy.KEEP,
            OneTimeWorkRequestBuilder<SyncWorker>()
                .setConstraints(Constraints(NetworkType.CONNECTED))
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, java.util.concurrent.TimeUnit.SECONDS)
                .build(),
        )
    }
}
