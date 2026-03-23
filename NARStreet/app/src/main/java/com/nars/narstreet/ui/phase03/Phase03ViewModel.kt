package com.nars.narstreet.ui.phase03

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.CityCenterEntity
import com.nars.narstreet.repository.AreaRepository
import com.nars.narstreet.repository.CityCenterRepository
import com.nars.narstreet.repository.CommuneRepository
import com.nars.narstreet.ui.components.CommuneMapContext
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.SyncState
import com.nars.narstreet.ui.components.parseCoordinatesJson
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject
import org.maplibre.android.geometry.LatLng

data class Phase03UiState(
    val cityCenter: CityCenterEntity?      = null,
    val isLoading: Boolean                 = true,
    val syncState: SyncState               = SyncState.IDLE,
    val communeContext: CommuneMapContext?  = null,
    val mapLayers: MapLayersState          = MapLayersState(),
    val username: String                   = "",
)

@HiltViewModel
class Phase03ViewModel @Inject constructor(
    private val repo: CityCenterRepository,
    private val areaRepo: AreaRepository,
    private val communeRepo: CommuneRepository,
    private val session: SessionManager,
    private val connectivity: ConnectivityObserver,
) : ViewModel() {

    private val _uiState = MutableStateFlow(Phase03UiState(
        mapLayers = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase03UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch { session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } } }
        viewModelScope.launch {
            session.communeCenter.collect { c ->
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(communeCenter = c)) }
            }
        }
        viewModelScope.launch {
            repo.cityCenter.collect { cc ->
                val point = if (cc != null) LatLng(cc.lat, cc.lng) else null
                _uiState.update { it.copy(
                    cityCenter = cc,
                    isLoading  = false,
                    mapLayers  = it.mapLayers.copy(cityCenterPoint = point),
                ) }
            }
        }
        // Areas shown as context behind the city center point (per spreadsheet)
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
            communeRepo.refresh()
        }
    }
}
