package com.nars.narstreet.ui.phase04

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.RoadEntity
import com.nars.narstreet.data.model.SyncStatus
import com.nars.narstreet.repository.*
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.NarsFeatureCounts
import com.nars.narstreet.ui.components.SyncState
import com.nars.narstreet.ui.components.parseCoordinatesJson
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase04UiState(
    val roads: List<RoadEntity>   = emptyList(),
    val isLoading: Boolean        = true,
    val syncState: SyncState      = SyncState.IDLE,
    val editingRoad: RoadEntity?  = null,
    val infoRoad: RoadEntity?     = null,
    val loadError: String?        = null,
    val mapLayers: MapLayersState = MapLayersState(),
    val counts: NarsFeatureCounts = NarsFeatureCounts(),
    val username: String          = "",
    val communeName: String       = "",
)

@HiltViewModel
class Phase04ViewModel @Inject constructor(
    private val roadRepo: RoadRepository,
    private val areaRepo: AreaRepository,
    private val cityCenterRepo: CityCenterRepository,
    private val countsRepo: FeatureCountsRepository,
    private val communeRepo: CommuneRepository,
    private val authRepo: AuthRepository,
    private val session: SessionManager,
    private val connectivity: ConnectivityObserver,
) : ViewModel() {

    private val _uiState = MutableStateFlow(Phase04UiState(
        mapLayers = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase04UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch {
            session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } }
        }
        viewModelScope.launch {
            session.communeName.collect { _uiState.update { s -> s.copy(communeName = it ?: "") } }
        }
        viewModelScope.launch {
            session.communeCenter.collect { c ->
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(communeCenter = c)) }
            }
        }
        viewModelScope.launch {
            roadRepo.roads.collect { roads ->
                val polylines = roads.map { parseCoordinatesJson(it.coordinatesJson) }
                _uiState.update { s -> s.copy(
                    roads     = roads,
                    isLoading = false,
                    mapLayers = s.mapLayers.copy(
                        roadPolylines = polylines,
                        roadDbIds     = roads.map { it.remoteId },
                        roadLabels    = roads.map { it.label },
                    ),
                ) }
            }
        }
        // Areas context layer (per spreadsheet)
        viewModelScope.launch {
            areaRepo.areas.collect { areas ->
                val polys  = areas.map { parseCoordinatesJson(it.coordinatesJson) }
                val labels = areas.map { it.label }
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(areaPolygons = polys, areaLabels = labels)) }
            }
        }
        // City center context layer (per spreadsheet)
        viewModelScope.launch {
            cityCenterRepo.cityCenter.collect { cc ->
                val point = if (cc != null) LatLng(cc.lat, cc.lng) else null
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(cityCenterPoint = point)) }
            }
        }
        viewModelScope.launch {
            countsRepo.counts.collect { counts ->
                _uiState.update { it.copy(counts = counts) }
            }
        }
        viewModelScope.launch {
            combine(roadRepo.pendingCount, connectivity.isOnline) { pending, online ->
                when {
                    pending > 0 && !online -> SyncState.OFFLINE
                    pending > 0 && online  -> SyncState.PENDING
                    else                   -> SyncState.IDLE
                }
            }.collect { s -> _uiState.update { it.copy(syncState = s) } }
        }
        viewModelScope.launch {
            communeRepo.context.collect { ctx ->
                _uiState.update { it.copy(mapLayers = it.mapLayers.copy(communeContext = ctx)) }
            }
        }
        viewModelScope.launch {
            roadRepo.lastError.collect { err ->
                _uiState.update { it.copy(loadError = err) }
            }
        }
        viewModelScope.launch {
            roadRepo.refresh()
            areaRepo.refresh()
            cityCenterRepo.refresh()
            communeRepo.refresh()
        }
    }

    fun startEditing(road: RoadEntity) {
        val highlight = parseCoordinatesJson(road.coordinatesJson).takeIf { it.isNotEmpty() }
        _uiState.update { it.copy(
            editingRoad = road,
            mapLayers   = it.mapLayers.copy(highlightPolyline = highlight),
        ) }
    }

    fun cancelEditing() {
        _uiState.update { it.copy(
            editingRoad = null,
            mapLayers   = it.mapLayers.copy(highlightPolyline = null),
        ) }
    }

    fun showInfo(road: RoadEntity) = _uiState.update { it.copy(infoRoad = road) }
    fun dismissInfo()              = _uiState.update { it.copy(infoRoad = null) }

    fun saveCharacteristics(
        road: RoadEntity,
        lanes: Int?,
        traffic: String?,
        trad: String?,
        median: Boolean?,
        greenery: Boolean?,
        deadEnd: Boolean?,
    ) {
        viewModelScope.launch {
            roadRepo.saveCharacteristics(road.copy(
                lanes           = lanes,
                trafficCapacity = traffic,
                tradActivity    = trad,
                hasMedianStrip  = median,
                hasGreenery     = greenery,
                isDeadEnd       = deadEnd,
                syncStatus      = SyncStatus.PENDING,
            ))
            _uiState.update { it.copy(
                editingRoad = null,
                mapLayers   = it.mapLayers.copy(highlightPolyline = null),
            ) }
        }
    }

    fun forceRefresh() {
        viewModelScope.launch { roadRepo.refresh() }
    }

    fun clearError() = _uiState.update { it.copy(loadError = null) }

    fun logout(onDone: () -> Unit) = viewModelScope.launch {
        authRepo.logout()
        onDone()
    }
}
