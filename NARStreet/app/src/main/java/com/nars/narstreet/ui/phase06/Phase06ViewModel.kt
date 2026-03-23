package com.nars.narstreet.ui.phase06

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.BuildingEntity
import com.nars.narstreet.repository.AreaRepository
import com.nars.narstreet.repository.BuildingRepository
import com.nars.narstreet.repository.CommuneRepository
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.SyncState
import com.nars.narstreet.ui.components.parseCoordinatesJson
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase06UiState(
    val buildings: List<BuildingEntity>   = emptyList(),
    val isLoading: Boolean                = true,
    val infoBuilding: BuildingEntity?     = null,
    val communeCenter: LatLng             = LatLng(36.7, 3.05),
    val mapLayers: MapLayersState         = MapLayersState(),
    val username: String                  = "",
    val syncState: SyncState              = SyncState.IDLE,
)

@HiltViewModel
class Phase06ViewModel @Inject constructor(
    private val repo: BuildingRepository,
    private val areaRepo: AreaRepository,
    private val communeRepo: CommuneRepository,
    private val connectivity: ConnectivityObserver,
    private val session: SessionManager,
) : ViewModel() {

    private val _uiState = MutableStateFlow(Phase06UiState(
        communeCenter = session.communeCenterState.value,
        mapLayers     = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase06UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch { session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } } }
        viewModelScope.launch {
            session.communeCenter.collect { c ->
                _uiState.update { s -> s.copy(communeCenter = c, mapLayers = s.mapLayers.copy(communeCenter = c)) }
            }
        }
        viewModelScope.launch {
            repo.buildings.collect { list ->
                // Buildings are POINT features — lat/lng, not polygons
                val points = list.map { LatLng(it.lat, it.lng) }
                _uiState.update { s -> s.copy(
                    buildings = list,
                    isLoading = false,
                    mapLayers = s.mapLayers.copy(
                        buildingPoints = points,
                        buildingLabels = list.map { it.label },
                        buildingDbIds  = list.map { it.id },
                    ),
                ) }
            }
        }
        // Areas shown as context (per spreadsheet: Phase06 shows Areas ✓)
        viewModelScope.launch {
            areaRepo.areas.collect { areas ->
                val polys  = areas.map { parseCoordinatesJson(it.coordinatesJson) }
                val labels = areas.map { it.label }
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(areaPolygons = polys, areaLabels = labels)) }
            }
        }
        viewModelScope.launch {
            combine(repo.pendingCount, connectivity.isOnline) { pending, online ->
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
        viewModelScope.launch { communeRepo.refresh() }
        viewModelScope.launch {
            repo.refresh()
            areaRepo.refresh()
            _uiState.update { it.copy(isLoading = false) }
        }
    }

    fun showInfo(b: BuildingEntity) = _uiState.update { it.copy(infoBuilding = b) }
    fun dismissInfo()               = _uiState.update { it.copy(infoBuilding = null) }
}
