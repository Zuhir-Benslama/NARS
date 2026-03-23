package com.nars.narstreet.ui.phase01

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.AreaEntity
import com.nars.narstreet.repository.AreaRepository
import com.nars.narstreet.repository.CommuneRepository
import com.nars.narstreet.ui.components.CommuneMapContext
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.SyncState
import com.nars.narstreet.ui.components.parseCoordinatesJson
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase01UiState(
    val areas: List<AreaEntity>      = emptyList(),
    val isLoading: Boolean           = true,
    val isSaved: Boolean             = false,
    val syncState: SyncState         = SyncState.IDLE,
    val editingArea: AreaEntity?     = null,
    val infoArea: AreaEntity?        = null,
    val vertices: List<LatLng>       = emptyList(),
    val communeContext: CommuneMapContext? = null,
    val communeCenter: LatLng        = LatLng(36.7, 3.05),
    // All area polygons for list-view map rendering
    val mapLayers: MapLayersState    = MapLayersState(),
    val username: String             = "",
)

@HiltViewModel
class Phase01ViewModel @Inject constructor(
    savedState: SavedStateHandle,
    private val repo: AreaRepository,
    private val communeRepo: CommuneRepository,
    private val session: SessionManager,
    private val connectivity: ConnectivityObserver,
) : ViewModel() {

    private val areaId: Long = savedState["areaId"] ?: 0L

    private val _uiState = MutableStateFlow(Phase01UiState(
        communeCenter = session.communeCenterState.value,
        mapLayers     = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase01UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch { session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } } }
        viewModelScope.launch {
            session.communeCenter.collect { c ->
                _uiState.update { s -> s.copy(communeCenter = c, mapLayers = s.mapLayers.copy(communeCenter = c)) }
            }
        }
        viewModelScope.launch {
            repo.areas.collect { list ->
                val editing = if (areaId > 0) list.find { it.id == areaId } else null
                // Build areaPolygons for list-view map — ALL areas, not just first
                val polys  = list.map { parseCoordinatesJson(it.coordinatesJson) }
                val labels = list.map { it.label }
                _uiState.update { s -> s.copy(
                    areas       = list,
                    isLoading   = false,
                    editingArea = editing,
                    vertices    = editing?.let { parseCoordinatesJson(it.coordinatesJson) } ?: s.vertices,
                    mapLayers   = s.mapLayers.copy(areaPolygons = polys, areaLabels = labels),
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
                _uiState.update { it.copy(communeContext = ctx, mapLayers = it.mapLayers.copy(communeContext = ctx)) }
            }
        }
        viewModelScope.launch {
            repo.refresh()
            _uiState.update { it.copy(isLoading = false) }
            communeRepo.refresh()
        }
    }

    fun onVerticesMoved(vertices: List<LatLng>) = _uiState.update { it.copy(vertices = vertices) }
    fun showInfo(area: AreaEntity) = _uiState.update { it.copy(infoArea = area) }
    fun dismissInfo()              = _uiState.update { it.copy(infoArea = null) }

    fun saveGeometry() {
        val area = _uiState.value.editingArea ?: return
        viewModelScope.launch {
            repo.saveGeometry(area.copy(coordinatesJson = serializeVertices(_uiState.value.vertices)))
            _uiState.update { it.copy(isSaved = true) }
        }
    }

    private fun serializeVertices(v: List<LatLng>): String =
        "[" + v.joinToString(",") { """{"lat":${it.latitude},"lng":${it.longitude}}""" } + "]"
}
