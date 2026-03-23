package com.nars.narstreet.repository

import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncScheduler
import com.nars.narstreet.core.util.toAnyDouble
import com.nars.narstreet.data.dao.CityCenterDao
import com.nars.narstreet.data.model.CityCenterEntity
import com.nars.narstreet.data.model.SyncStatus
import kotlinx.coroutines.flow.Flow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class CityCenterRepository @Inject constructor(
    private val dao: CityCenterDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val syncScheduler: SyncScheduler,
) {
    val cityCenter: Flow<CityCenterEntity?> = dao.get()
    val pendingCount: Flow<Int>             = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadLayer("city_center")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                CityCenterEntity(
                    id         = existing?.id ?: 0,
                    remoteId   = f.id,
                    label      = f.label,
                    // Backend stores city_center as flat {lat, lng} — use toAnyDouble()
                    lat        = f.data.parse()["lat"].toAnyDouble(),
                    lng        = f.data.parse()["lng"].toAnyDouble(),
                    dataJson   = f.data.json,
                    syncStatus = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (_: Exception) { }
    }

    suspend fun save(entity: CityCenterEntity) {
        dao.update(entity.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
    }
}
