package com.nars.narstreet.ui.phase02

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.DistrictEntity
import com.nars.narstreet.repository.AreaRepository
import com.nars.narstreet.repository.CommuneRepository
import com.nars.narstreet.repository.DistrictRepository
import com.nars.narstreet.ui.components.CommuneMapContext
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.SyncState
import com.nars.narstreet.ui.components.parseCoordinatesJson
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase02UiState(
    val districts: List<DistrictEntity>   = emptyList(),
    val isLoading: Boolean                = true,
    val isSaved: Boolean                  = false,
    val syncState: SyncState              = SyncState.IDLE,
    val editingDistrict: DistrictEntity?  = null,
    val infoDistrict: DistrictEntity?     = null,
    val vertices: List<LatLng>            = emptyList(),
    val communeContext: CommuneMapContext? = null,
    val communeCenter: LatLng             = LatLng(36.7, 3.05),
    val mapLayers: MapLayersState         = MapLayersState(),
    val username: String                  = "",
)

@HiltViewModel
class Phase02ViewModel @Inject constructor(
    savedState: SavedStateHandle,
    private val repo: DistrictRepository,
    private val areaRepo: AreaRepository,
    private val communeRepo: CommuneRepository,
    private val session: SessionManager,
    private val connectivity: ConnectivityObserver,
) : ViewModel() {

    private val districtId: Long = savedState["districtId"] ?: 0L

    private val _uiState = MutableStateFlow(Phase02UiState(
        communeCenter = session.communeCenterState.value,
        mapLayers     = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase02UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch { session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } } }
        viewModelScope.launch {
            session.communeCenter.collect { c ->
                _uiState.update { s -> s.copy(communeCenter = c, mapLayers = s.mapLayers.copy(communeCenter = c)) }
            }
        }
        viewModelScope.launch {
            repo.districts.collect { list ->
                val editing = if (districtId > 0) list.find { it.id == districtId } else null
                _uiState.update { s -> s.copy(
                    districts       = list,
                    isLoading       = false,
                    editingDistrict = editing,
                    vertices        = editing?.let { parseCoordinatesJson(it.coordinatesJson) } ?: s.vertices,
                ) }
            }
        }
        // Areas shown as read-only context behind districts (per spreadsheet)
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
                _uiState.update { it.copy(communeContext = ctx, mapLayers = it.mapLayers.copy(communeContext = ctx)) }
            }
        }
        viewModelScope.launch {
            repo.refresh()
            areaRepo.refresh()
            _uiState.update { it.copy(isLoading = false) }
            communeRepo.refresh()
        }
    }

    fun onVerticesMoved(vertices: List<LatLng>) = _uiState.update { it.copy(vertices = vertices) }
    fun showInfo(district: DistrictEntity) = _uiState.update { it.copy(infoDistrict = district) }
    fun dismissInfo() = _uiState.update { it.copy(infoDistrict = null) }

    fun saveGeometry() {
        val district = _uiState.value.editingDistrict ?: return
        viewModelScope.launch {
            repo.saveGeometry(district.copy(coordinatesJson = serializeVertices(_uiState.value.vertices)))
            _uiState.update { it.copy(isSaved = true) }
        }
    }

    private fun serializeVertices(v: List<LatLng>): String =
        "[" + v.joinToString(",") { """{"lat":${it.latitude},"lng":${it.longitude}}""" } + "]"
}
