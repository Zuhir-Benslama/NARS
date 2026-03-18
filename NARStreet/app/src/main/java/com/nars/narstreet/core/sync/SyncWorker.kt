package com.nars.narstreet.core.sync

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.nars.narstreet.data.dao.BuildingDao
import com.nars.narstreet.data.dao.EntranceDao
import com.nars.narstreet.data.dao.PanelDao
import com.nars.narstreet.data.dao.RoadDao
import com.nars.narstreet.data.dao.SpaceDao
import com.nars.narstreet.data.model.SyncStatus
import com.nars.narstreet.core.network.ApiService
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject

@HiltWorker
class SyncWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters,
    private val api: ApiService,
    private val roadDao: RoadDao,
    private val entranceDao: EntranceDao,
    private val buildingDao: BuildingDao,
    private val spaceDao: SpaceDao,
    private val panelDao: PanelDao,
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        return try {
            syncRoads()
            syncEntrances()
            syncBuildings()
            syncSpaces()
            syncPanels()
            Result.success()
        } catch (e: Exception) {
            if (runAttemptCount < 3) Result.retry() else Result.failure()
        }
    }

    private suspend fun syncRoads() {
        roadDao.getPending().forEach { entity ->
            try {
                api.updateRoad(entity.remoteId, entity.toUpdateDto())
                roadDao.markSynced(entity.id)
            } catch (_: Exception) {
                roadDao.markError(entity.id)
            }
        }
    }

    private suspend fun syncEntrances() {
        entranceDao.getPending().forEach { entity ->
            try {
                if (entity.remoteId == 0L) {
                    val result = api.saveEntrance(entity.toSaveDto())
                    entranceDao.updateRemoteId(entity.id, result.id)
                } else {
                    api.updateEntrance(entity.remoteId, entity.toUpdateDto())
                }
                entranceDao.markSynced(entity.id)
            } catch (_: Exception) {
                entranceDao.markError(entity.id)
            }
        }
    }

    private suspend fun syncBuildings() {
        buildingDao.getPending().forEach { entity ->
            try {
                api.updateBuilding(entity.remoteId, entity.toUpdateDto())
                buildingDao.markSynced(entity.id)
            } catch (_: Exception) {
                buildingDao.markError(entity.id)
            }
        }
    }

    private suspend fun syncSpaces() {
        spaceDao.getPending().forEach { entity ->
            try {
                api.updateSpace(entity.remoteId, entity.toUpdateDto())
                spaceDao.markSynced(entity.id)
            } catch (_: Exception) {
                spaceDao.markError(entity.id)
            }
        }
    }

    private suspend fun syncPanels() {
        panelDao.getPending().forEach { entity ->
            try {
                api.updatePanel(entity.remoteId, entity.toUpdateDto())
                panelDao.markSynced(entity.id)
            } catch (_: Exception) {
                panelDao.markError(entity.id)
            }
        }
    }

    companion object {
        const val TAG = "NARStreetSync"
    }
}
