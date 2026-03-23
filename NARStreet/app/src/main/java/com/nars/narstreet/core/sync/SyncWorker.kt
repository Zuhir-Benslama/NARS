package com.nars.narstreet.core.sync

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.data.dao.*
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject

@HiltWorker
class SyncWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters,
    private val api: ApiService,
    private val areaDao: AreaDao,
    private val districtDao: DistrictDao,
    private val roadDao: RoadDao,
    private val entranceDao: EntranceDao,
    private val buildingDao: BuildingDao,
    private val spaceDao: SpaceDao,
    private val panelDao: PanelDao,
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        // On retry attempts, reset ERROR entities back to PENDING so they are
        // picked up again by getPending(). Without this, errored entities are
        // permanently stuck once the first attempt marks them ERROR.
        if (runAttemptCount > 0) resetErrors()

        return try {
            val errors = syncAreas() +
                syncDistricts() +
                syncRoads() +
                syncEntrances() +
                syncBuildings() +
                syncSpaces() +
                syncPanels()

            when {
                errors == 0         -> Result.success()
                runAttemptCount < 3 -> Result.retry()
                else                -> Result.failure()
            }
        } catch (e: Exception) {
            if (runAttemptCount < 3) Result.retry() else Result.failure()
        }
    }

    /** Reset ERROR → PENDING so retry attempts re-process previously failed entities. */
    private suspend fun resetErrors() {
        areaDao.resetErrors()
        districtDao.resetErrors()
        roadDao.resetErrors()
        entranceDao.resetErrors()
        buildingDao.resetErrors()
        spaceDao.resetErrors()
        panelDao.resetErrors()
    }

    /** Returns the number of entities that failed to sync. */
    private suspend fun syncAreas(): Int {
        var errors = 0
        areaDao.getPending().forEach { entity ->
            try {
                // Use the generic updateFeature — areas are not roads
                api.updateFeature(entity.remoteId, entity.toUpdateDto())
                areaDao.markSynced(entity.id)
            } catch (_: Exception) {
                areaDao.markError(entity.id)
                errors++
            }
        }
        return errors
    }

    private suspend fun syncDistricts(): Int {
        var errors = 0
        districtDao.getPending().forEach { entity ->
            try {
                api.updateFeature(entity.remoteId, entity.toUpdateDto())
                districtDao.markSynced(entity.id)
            } catch (_: Exception) {
                districtDao.markError(entity.id)
                errors++
            }
        }
        return errors
    }

    private suspend fun syncRoads(): Int {
        var errors = 0
        roadDao.getPending().forEach { entity ->
            try {
                api.updateFeature(entity.remoteId, entity.toUpdateDto())
                roadDao.markSynced(entity.id)
            } catch (_: Exception) {
                roadDao.markError(entity.id)
                errors++
            }
        }
        return errors
    }

    private suspend fun syncEntrances(): Int {
        var errors = 0
        entranceDao.getPending().forEach { entity ->
            try {
                if (entity.remoteId == 0L) {
                    val result = api.saveEntrance(entity.toSaveDto())
                    entranceDao.updateRemoteId(entity.id, result.id)
                } else {
                    api.updateFeature(entity.remoteId, entity.toUpdateDto())
                }
                entranceDao.markSynced(entity.id)
            } catch (_: Exception) {
                entranceDao.markError(entity.id)
                errors++
            }
        }
        return errors
    }

    private suspend fun syncBuildings(): Int {
        var errors = 0
        buildingDao.getPending().forEach { entity ->
            try {
                api.updateFeature(entity.remoteId, entity.toUpdateDto())
                buildingDao.markSynced(entity.id)
            } catch (_: Exception) {
                buildingDao.markError(entity.id)
                errors++
            }
        }
        return errors
    }

    private suspend fun syncSpaces(): Int {
        var errors = 0
        spaceDao.getPending().forEach { entity ->
            try {
                api.updateFeature(entity.remoteId, entity.toUpdateDto())
                spaceDao.markSynced(entity.id)
            } catch (_: Exception) {
                spaceDao.markError(entity.id)
                errors++
            }
        }
        return errors
    }

    private suspend fun syncPanels(): Int {
        var errors = 0
        panelDao.getPending().forEach { entity ->
            try {
                api.updateFeature(entity.remoteId, entity.toUpdateDto())
                panelDao.markSynced(entity.id)
            } catch (_: Exception) {
                panelDao.markError(entity.id)
                errors++
            }
        }
        return errors
    }

    companion object {
        const val TAG = "NARStreetSync"
    }
}
