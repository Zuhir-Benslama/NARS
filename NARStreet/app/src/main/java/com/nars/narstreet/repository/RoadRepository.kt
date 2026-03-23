package com.nars.narstreet.repository

import android.util.Log
import com.nars.narstreet.core.network.ApiService
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.core.sync.SyncScheduler
import com.nars.narstreet.core.util.extractCoords
import com.nars.narstreet.data.dao.RoadDao
import com.nars.narstreet.data.model.RoadEntity
import com.nars.narstreet.data.model.SyncStatus
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class RoadRepository @Inject constructor(
    private val dao: RoadDao,
    private val api: ApiService,
    private val connectivity: ConnectivityObserver,
    private val syncScheduler: SyncScheduler,
) {
    val roads: Flow<List<RoadEntity>> = dao.getAll()
    val pendingCount: Flow<Int>       = dao.pendingCount()

    private val _lastError = MutableStateFlow<String?>(null)
    val lastError: StateFlow<String?> = _lastError.asStateFlow()


    private val roadLayers = listOf(
        "boulevard", "avenue", "street", "drive", "lane", "cul_de_sac", "way"
    )

    suspend fun refresh() {
        _lastError.value = null
        try {
            Log.d("NARStreet", "RoadRepo: starting refresh")
            val features = try {
                Log.d("NARStreet", "RoadRepo: trying loadByType(road)")
                api.loadByType("road")
            } catch (e: Exception) {
                Log.e("NARStreet", "RoadRepo: loadByType failed: ${e.message}")
                _lastError.value = "loadByType failed: ${e.message}, trying sub-layers..."
                roadLayers.flatMap { layer ->
                    try { api.loadLayer(layer) }
                    catch (_: Exception) { emptyList() }
                }
            }

            Log.d("NARStreet", "RoadRepo: fetched ${features.size} features")
            if (features.isEmpty()) {
                _lastError.value = "No roads returned from server (check loadByType or sub-layers)"
                return
            }

            val entities = features.map { f ->
                val existing   = dao.getByRemoteId(f.id)
                val parsed     = f.data.parse()
                val coordsJson = extractCoords(parsed)
                RoadEntity(
                    id              = existing?.id ?: 0,
                    remoteId        = f.id,
                    label           = f.label,
                    layer           = f.layer,
                    coordinatesJson = coordsJson,
                    dataJson        = f.data.json,
                    lanes           = existing?.lanes,
                    trafficCapacity = existing?.trafficCapacity,
                    tradActivity    = existing?.tradActivity,
                    hasMedianStrip  = existing?.hasMedianStrip,
                    hasGreenery     = existing?.hasGreenery,
                    isDeadEnd       = existing?.isDeadEnd,
                    syncStatus      = existing?.syncStatus ?: SyncStatus.SYNCED,
                )
            }
            Log.d("NARStreet", "RoadRepo: inserting ${entities.size} roads. First coordsJson: ${entities.firstOrNull()?.coordinatesJson?.take(80)}")
            dao.insertAll(entities)
            _lastError.value = null

        } catch (e: Exception) {
            Log.e("NARStreet", "RoadRepo: refresh exception: ${e.message}", e)
            _lastError.value = "Refresh failed: ${e.message}"
        }
    }

    suspend fun saveCharacteristics(road: RoadEntity) {
        dao.update(road.copy(syncStatus = SyncStatus.PENDING))
        if (connectivity.isCurrentlyOnline()) syncScheduler.schedule()
    }
}
