package com.nars.narstreet.repository

import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncScheduler
import com.nars.narstreet.core.util.extractCoords
import com.nars.narstreet.data.dao.AreaDao
import com.nars.narstreet.data.model.AreaEntity
import com.nars.narstreet.data.model.SyncStatus
import kotlinx.coroutines.flow.Flow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class AreaRepository @Inject constructor(
    private val dao: AreaDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val syncScheduler: SyncScheduler,
) {
    val areas: Flow<List<AreaEntity>> = dao.getAll()
    val pendingCount: Flow<Int>       = dao.pendingCount()

    suspend fun refresh() {
        try {
            val features = api.loadByType("area")
            dao.insertAll(features.map { f ->
                val existing = dao.getByRemoteId(f.id)
                AreaEntity(
                    id              = existing?.id ?: 0,
                    remoteId        = f.id,
                    label           = f.label,
                    layer           = f.layer,
                    coordinatesJson = extractCoords(f.data.parse()),
                    dataJson        = f.data.json,
                    syncStatus      = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            })
        } catch (_: Exception) { }
    }

    suspend fun saveGeometry(area: AreaEntity) {
        dao.update(area.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
    }
}
