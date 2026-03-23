package com.nars.narstreet.ui.phase08

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.PanelEntity
import com.nars.narstreet.repository.*
import com.nars.narstreet.ui.components.parseCoordinatesJson
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.SyncState
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase08UiState(
    val panel: PanelEntity?          = null,
    val panels: List<PanelEntity>    = emptyList(),   // for list view
    val isLoading: Boolean           = true,
    val isSaved: Boolean             = false,
    val infoPanel: PanelEntity?      = null,
    val mapLayers: MapLayersState    = MapLayersState(),
    val username: String             = "",
    val syncState: SyncState         = SyncState.IDLE,
)

@HiltViewModel
class Phase08ViewModel @Inject constructor(
    savedState: SavedStateHandle,
    private val repo: PanelRepository,
    private val areaRepo: AreaRepository,
    private val districtRepo: DistrictRepository,
    private val roadRepo: RoadRepository,
    private val buildingRepo: BuildingRepository,
    private val spaceRepo: SpaceRepository,
    private val communeRepo: CommuneRepository,
    private val connectivity: ConnectivityObserver,
    private val session: SessionManager,
) : ViewModel() {

    private val panelId: Long = savedState["panelId"] ?: 0L

    private val _uiState = MutableStateFlow(Phase08UiState(
        mapLayers = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase08UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch { session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } } }

        viewModelScope.launch {
            repo.panels.collect { list ->
                val panel  = if (panelId > 0) list.find { it.id == panelId } else null
                val points = list.map { LatLng(it.lat, it.lng) }
                val labels = list.map { it.label }
                _uiState.update { s -> s.copy(
                    panels    = list,
                    panel     = panel,
                    isLoading = false,
                    mapLayers = s.mapLayers.copy(panelPoints = points, panelLabels = labels),
                ) }
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
        viewModelScope.launch {
            roadRepo.roads.collect { roads ->
                val polys = roads.map { parseCoordinatesJson(it.coordinatesJson) }
                _uiState.update { it.copy(mapLayers = it.mapLayers.copy(roadPolylines = polys)) }
            }
        }
        viewModelScope.launch {
            buildingRepo.buildings.collect { buildings ->
                // Buildings are POINT features — lat/lng, not polygons
                val points = buildings.map { LatLng(it.lat, it.lng) }
                val labels = buildings.map { it.label }
                val ids    = buildings.map { it.id }
                _uiState.update { it.copy(mapLayers = it.mapLayers.copy(
                    buildingPoints = points,
                    buildingLabels = labels,
                    buildingDbIds  = ids,
                )) }
            }
        }
        viewModelScope.launch {
            spaceRepo.spaces.collect { spaces ->
                val polys = spaces.map { parseCoordinatesJson(it.coordinatesJson) }
                val lbls  = spaces.map { it.label }
                _uiState.update { it.copy(mapLayers = it.mapLayers.copy(spacePolygons = polys, spaceLabels = lbls)) }
            }
        }
        viewModelScope.launch {
            districtRepo.districts.collect { districts ->
                val polys = districts.map { parseCoordinatesJson(it.coordinatesJson) }
                val lbls  = districts.map { it.label }
                _uiState.update { it.copy(mapLayers = it.mapLayers.copy(districtPolygons = polys, districtLabels = lbls)) }
            }
        }
        // Areas shown as context (per spreadsheet: Phase08 shows Areas ✓)
        viewModelScope.launch {
            areaRepo.areas.collect { areas ->
                val polys  = areas.map { parseCoordinatesJson(it.coordinatesJson) }
                val labels = areas.map { it.label }
                _uiState.update { it.copy(mapLayers = it.mapLayers.copy(areaPolygons = polys, areaLabels = labels)) }
            }
        }
        viewModelScope.launch { communeRepo.refresh() }
        viewModelScope.launch {
            repo.refresh()
            areaRepo.refresh()
            districtRepo.refresh()
            roadRepo.refresh()
            buildingRepo.refresh()
            spaceRepo.refresh()
            _uiState.update { it.copy(isLoading = false) }
        }
    }

    fun showInfo(p: PanelEntity) = _uiState.update { it.copy(infoPanel = p) }
    fun dismissInfo()            = _uiState.update { it.copy(infoPanel = null) }

    fun onPanelMoved(newPos: LatLng) {
        val panel = _uiState.value.panel ?: return
        _uiState.update { it.copy(panel = panel.copy(lat = newPos.latitude, lng = newPos.longitude)) }
    }

    fun saveChecklist(isPlaced: Boolean, isCorrectLocation: Boolean?) {
        val panel = _uiState.value.panel ?: return
        viewModelScope.launch {
            repo.saveChecklist(panel.copy(
                isPlaced          = isPlaced,
                isCorrectLocation = isCorrectLocation,
                orderNeeded       = !isPlaced,
                relocateNeeded    = isPlaced && isCorrectLocation == false,
            ))
            _uiState.update { it.copy(isSaved = true) }
        }
    }
}
