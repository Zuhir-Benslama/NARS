package com.nars.narstreet.ui.phase05

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.location.LocationClient
import com.nars.narstreet.core.session.SessionManager
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.EntranceEntity
import com.nars.narstreet.repository.AreaRepository
import com.nars.narstreet.repository.CityCenterRepository
import com.nars.narstreet.repository.EntranceRepository
import com.nars.narstreet.repository.RoadRepository
import com.nars.narstreet.ui.components.MapLayersState
import com.nars.narstreet.ui.components.SyncState
import com.nars.narstreet.ui.components.parseCoordinatesJson
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject

data class Phase05UiState(
    val entrances: List<EntranceEntity>  = emptyList(),
    val isLoading: Boolean               = true,
    val username: String                 = "",
    val syncState: SyncState             = SyncState.IDLE,
    val currentLat: Double               = 36.7,
    val currentLng: Double               = 3.05,
    val locationPermission: Boolean      = false,
    val editingEntrance: EntranceEntity? = null,
    val captureSuccess: Boolean          = false,
    val mapLayers: MapLayersState        = MapLayersState(),
)

@HiltViewModel
class Phase05ViewModel @Inject constructor(
    private val entranceRepo: EntranceRepository,
    private val roadRepo: RoadRepository,
    private val cityCenterRepo: CityCenterRepository,
    private val areaRepo: AreaRepository,
    private val locationClient: LocationClient,
    private val connectivity: ConnectivityObserver,
    private val session: SessionManager,
    @ApplicationContext private val context: Context,
) : ViewModel() {

    private val _uiState = MutableStateFlow(Phase05UiState(
        mapLayers = MapLayersState(communeCenter = session.communeCenterState.value),
    ))
    val uiState: StateFlow<Phase05UiState> = _uiState.asStateFlow()

    init {
        val hasPermission = ContextCompat.checkSelfPermission(
            context, Manifest.permission.ACCESS_FINE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED
        _uiState.update { it.copy(locationPermission = hasPermission) }

        viewModelScope.launch { session.username.collect { _uiState.update { s -> s.copy(username = it ?: "") } } }
        viewModelScope.launch {
            session.communeCenter.collect { c ->
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(communeCenter = c)) }
            }
        }

        viewModelScope.launch {
            entranceRepo.entrances.collect { list ->
                _uiState.update { it.copy(entrances = list, isLoading = false) }
            }
        }

        // Roads shown as context behind entrances (per spreadsheet)
        viewModelScope.launch {
            roadRepo.roads.collect { roads ->
                val polys = roads.map { parseCoordinatesJson(it.coordinatesJson) }
                val ids   = roads.map { it.remoteId }
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(
                    roadPolylines = polys,
                    roadDbIds     = ids,
                )) }
            }
        }

        // City center shown as context (per spreadsheet)
        viewModelScope.launch {
            cityCenterRepo.cityCenter.collect { cc ->
                val point = if (cc != null) LatLng(cc.lat, cc.lng) else null
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(cityCenterPoint = point)) }
            }
        }

        viewModelScope.launch {
            combine(entranceRepo.pendingCount, connectivity.isOnline) { pending, online ->
                when {
                    pending > 0 && !online -> SyncState.OFFLINE
                    pending > 0 && online  -> SyncState.PENDING
                    else                   -> SyncState.IDLE
                }
            }.collect { s -> _uiState.update { it.copy(syncState = s) } }
        }

        if (hasPermission) startLocationUpdates()
        // Areas shown as context (per spreadsheet: Phase05)
        viewModelScope.launch {
            areaRepo.areas.collect { areas ->
                val polys  = areas.map { parseCoordinatesJson(it.coordinatesJson) }
                val labels = areas.map { it.label }
                _uiState.update { s -> s.copy(mapLayers = s.mapLayers.copy(areaPolygons = polys, areaLabels = labels)) }
            }
        }
        viewModelScope.launch {
            entranceRepo.refresh()
            roadRepo.refresh()
            cityCenterRepo.refresh()
            areaRepo.refresh()
        }
    }

    fun onPermissionGranted() {
        _uiState.update { it.copy(locationPermission = true) }
        startLocationUpdates()
    }

    private fun startLocationUpdates() = viewModelScope.launch {
        locationClient.locationUpdates().collect { loc ->
            _uiState.update { it.copy(currentLat = loc.latitude, currentLng = loc.longitude) }
        }
    }

    fun captureEntrance() = viewModelScope.launch {
        val s = _uiState.value
        entranceRepo.captureEntrance(s.currentLat, s.currentLng, roadDbId = null)
        _uiState.update { it.copy(captureSuccess = true) }
    }

    fun dismissCaptureSuccess() = _uiState.update { it.copy(captureSuccess = false) }

    fun startEditing(entrance: EntranceEntity) = _uiState.update { it.copy(editingEntrance = entrance) }

    fun cancelEditing() = _uiState.update { it.copy(editingEntrance = null) }

    fun saveNumberingCheck(entrance: EntranceEntity, isNumbered: Boolean, isCorrect: Boolean?) {
        viewModelScope.launch {
            entranceRepo.saveNumberingCheck(
                entrance.copy(isNumbered = isNumbered, isNumberCorrect = isCorrect)
            )
            _uiState.update { it.copy(editingEntrance = null) }
        }
    }
}
