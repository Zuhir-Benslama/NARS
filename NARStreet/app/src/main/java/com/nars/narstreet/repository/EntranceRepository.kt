package com.nars.narstreet.repository

import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncScheduler
import com.nars.narstreet.core.util.toAnyDouble
import com.nars.narstreet.core.util.toAnyInt
import com.nars.narstreet.core.util.toAnyLong
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
    private val syncScheduler: SyncScheduler,
) {
    val entrances: Flow<List<EntranceEntity>> = dao.getAll()
    val pendingCount: Flow<Int>               = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadByType("house_entrance")
            val entities = features.map { f ->
                val data     = f.data.parse()
                val existing = dao.getByRemoteId(f.id)
                EntranceEntity(
                    id              = existing?.id ?: 0,
                    remoteId        = f.id,
                    label           = f.label,
                    // Backend stores entrances as flat {lat, lng} — no "coordinates" key.
                    // Use toAnyDouble() because the backend may send 36 (Long) or 36.7 (Double).
                    lat             = data["lat"].toAnyDouble(),
                    lng             = data["lng"].toAnyDouble(),
                    roadDbId        = data["roadDbId"]?.toAnyLong(),
                    entranceNumber  = data["entranceNumber"]?.toAnyInt(),
                    entranceTypeKey = f.layer,
                    // Preserve local field edits
                    isNumbered       = existing?.isNumbered,
                    isNumberCorrect  = existing?.isNumberCorrect,
                    orderPlateNeeded = existing?.orderPlateNeeded ?: false,
                    relocateNeeded   = existing?.relocateNeeded   ?: false,
                    syncStatus       = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            }
            dao.insertAll(entities)
        } catch (_: Exception) { }
    }

    suspend fun captureEntrance(lat: Double, lng: Double, roadDbId: Long?): Long {
        val entity = EntranceEntity(
            label          = "Entrance",
            lat            = lat,
            lng            = lng,
            roadDbId       = roadDbId,
            entranceNumber = null,
            syncStatus     = SyncStatus.PENDING,
        )
        val localId = dao.insert(entity)
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
        return localId
    }

    suspend fun saveNumberingCheck(entrance: EntranceEntity) {
        dao.update(entrance.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
    }
}
