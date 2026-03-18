package com.nars.narstreet.ui.phase06

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.BuildingEntity
import com.nars.narstreet.repository.BuildingRepository
import com.nars.narstreet.ui.components.SyncState
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase06UiState(
    val building: BuildingEntity? = null,
    val vertices: List<LatLng>    = emptyList(),
    val isLoading: Boolean        = true,
    val isSaved: Boolean          = false,
    val syncState: SyncState      = SyncState.IDLE,
)

@HiltViewModel
class Phase06ViewModel @Inject constructor(
    savedState: SavedStateHandle,
    private val repo: BuildingRepository,
    private val connectivity: ConnectivityObserver,
) : ViewModel() {

    private val buildingId: Long = savedState["buildingId"] ?: 0L

    private val _uiState = MutableStateFlow(Phase06UiState())
    val uiState: StateFlow<Phase06UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch {
            repo.buildings.collect { list ->
                val building = list.find { it.id == buildingId }
                if (building != null) {
                    _uiState.update {
                        it.copy(
                            building  = building,
                            vertices  = parseVertices(building.coordinatesJson),
                            isLoading = false,
                        )
                    }
                } else {
                    _uiState.update { it.copy(isLoading = false) }
                }
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
    }

    fun onVerticesMoved(vertices: List<LatLng>) {
        _uiState.update { it.copy(vertices = vertices) }
    }

    fun saveGeometry() {
        val building  = _uiState.value.building ?: return
        val vertices  = _uiState.value.vertices
        viewModelScope.launch {
            repo.saveGeometry(
                building.copy(coordinatesJson = serializeVertices(vertices))
            )
            _uiState.update { it.copy(isSaved = true) }
        }
    }

    private fun parseVertices(json: String): List<LatLng> = try {
        val trimmed = json.trim().removePrefix("[").removeSuffix("]")
        if (trimmed.isBlank()) return emptyList()
        trimmed.split("},{").map { entry ->
            val lat = Regex("\"lat\":([-\\d.]+)").find(entry)?.groupValues?.get(1)?.toDouble() ?: 0.0
            val lng = Regex("\"lng\":([-\\d.]+)").find(entry)?.groupValues?.get(1)?.toDouble() ?: 0.0
            LatLng(lat, lng)
        }
    } catch (_: Exception) { emptyList() }

    private fun serializeVertices(vertices: List<LatLng>): String =
        "[" + vertices.joinToString(",") { """{"lat":${it.latitude},"lng":${it.longitude}}""" } + "]"
}
