package com.nars.narstreet.ui.phase07

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.SpaceEntity
import com.nars.narstreet.repository.AreaRepository
import com.nars.narstreet.repository.CommuneRepository
import com.nars.narstreet.repository.SpaceRepository
import com.nars.narstreet.ui.components.CommuneMapContext
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.SyncState
import com.nars.narstreet.ui.components.parseCoordinatesJson
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase07UiState(
    val space: SpaceEntity?               = null,
    val spaces: List<SpaceEntity>         = emptyList(),
    val vertices: List<LatLng>            = emptyList(),
    val isLoading: Boolean                = true,
    val isSaved: Boolean                  = false,
    val infoSpace: SpaceEntity?           = null,
    val communeContext: CommuneMapContext? = null,
    val communeCenter: LatLng             = LatLng(36.7, 3.05),
    val mapLayers: MapLayersState         = MapLayersState(),
    val username: String                  = "",
    val syncState: SyncState              = SyncState.IDLE,
)

@HiltViewModel
class Phase07ViewModel @Inject constructor(
    savedState: SavedStateHandle,
    private val repo: SpaceRepository,
    private val areaRepo: AreaRepository,
    private val communeRepo: CommuneRepository,
    private val connectivity: ConnectivityObserver,
    private val session: SessionManager,
) : ViewModel() {

    private val spaceId: Long = savedState["spaceId"] ?: 0L

    private val _uiState = MutableStateFlow(Phase07UiState(
        communeCenter = session.communeCenterState.value,
        mapLayers     = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase07UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch { session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } } }
        viewModelScope.launch {
            session.communeCenter.collect { c ->
                _uiState.update { s -> s.copy(communeCenter = c, mapLayers = s.mapLayers.copy(communeCenter = c)) }
            }
        }
        viewModelScope.launch {
            repo.spaces.collect { list ->
                val space = if (spaceId > 0) list.find { it.id == spaceId } else null
                _uiState.update { s -> s.copy(
                    spaces    = list,
                    space     = space,
                    isLoading = false,
                    vertices  = space?.let { parseCoordinatesJson(it.coordinatesJson) } ?: s.vertices,
                ) }
            }
        }
        // Areas shown as context (per spreadsheet: Phase07 shows Areas ✓, Roads ✗)
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
        viewModelScope.launch { communeRepo.refresh() }
        viewModelScope.launch {
            repo.refresh()
            areaRepo.refresh()
            _uiState.update { it.copy(isLoading = false) }
        }
    }

    fun onVerticesMoved(vertices: List<LatLng>) = _uiState.update { it.copy(vertices = vertices) }
    fun showInfo(s: SpaceEntity) = _uiState.update { it.copy(infoSpace = s) }
    fun dismissInfo()            = _uiState.update { it.copy(infoSpace = null) }

    fun saveGeometry() {
        val space = _uiState.value.space ?: return
        viewModelScope.launch {
            repo.saveGeometry(space.copy(coordinatesJson = serializeVertices(_uiState.value.vertices)))
            _uiState.update { it.copy(isSaved = true) }
        }
    }

    private fun serializeVertices(v: List<LatLng>): String =
        "[" + v.joinToString(",") { """{"lat":${it.latitude},"lng":${it.longitude}}""" } + "]"
}
