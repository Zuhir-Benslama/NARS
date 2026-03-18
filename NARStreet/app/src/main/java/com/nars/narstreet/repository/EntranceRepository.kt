package com.nars.narstreet.repository

import androidx.work.*
import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncWorker
import com.nars.narstreet.data.dao.EntranceDao
import com.nars.narstreet.data.model.EntranceEntity
import com.nars.narstreet.data.model.SyncStatus
import kotlinx.coroutines.flow.Flow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class EntranceRepository @Inject constructor(
    private val dao: EntranceDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val workManager: WorkManager,
) {
    val entrances: Flow<List<EntranceEntity>> = dao.getAll()
    val pendingCount: Flow<Int>               = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadLayer("main_entrance") + api.loadLayer("secondary_entrance")
            val entities = features.map { f ->
                val data = f.data
                EntranceEntity(
                    remoteId        = f.id,
                    label           = f.label,
                    lat             = (data["lat"] as? Double) ?: 0.0,
                    lng             = (data["lng"] as? Double) ?: 0.0,
                    roadDbId        = (data["roadDbId"] as? Double)?.toLong(),
                    entranceNumber  = (data["entranceNumber"] as? Double)?.toInt(),
                    entranceTypeKey = f.layer,
                    syncStatus      = SyncStatus.SYNCED,
                )
            }
            dao.insertAll(entities)
        } catch (_: Exception) { }
    }

    /** Capture current GPS position as a new main entrance. */
    suspend fun captureEntrance(lat: Double, lng: Double, roadDbId: Long?): Long {
        val entity = EntranceEntity(
            label      = "Entrance",
            lat        = lat,
            lng        = lng,
            roadDbId   = roadDbId,
            syncStatus = SyncStatus.PENDING,
        )
        val localId = dao.insert(entity)
        if (connectivity.isCurrentlyOnline()) enqueueSyncWork()
        return localId
    }

    suspend fun saveNumberingCheck(entrance: EntranceEntity) {
        dao.update(entrance.copy(syncStatus = SyncStatus.PENDING))
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
