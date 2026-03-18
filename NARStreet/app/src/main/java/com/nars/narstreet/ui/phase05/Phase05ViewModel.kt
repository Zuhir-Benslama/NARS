package com.nars.narstreet.ui.phase05

import android.Manifest
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.location.LocationClient
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.EntranceEntity
import com.nars.narstreet.repository.EntranceRepository
import com.nars.narstreet.ui.components.SyncState
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import android.content.Context
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject

data class Phase05UiState(
    val entrances: List<EntranceEntity> = emptyList(),
    val isLoading: Boolean              = true,
    val syncState: SyncState            = SyncState.IDLE,
    val currentLat: Double              = 36.7,   // default: Algeria center
    val currentLng: Double              = 3.05,
    val locationPermission: Boolean     = false,
    val editingEntrance: EntranceEntity? = null,
    val captureSuccess: Boolean         = false,
)

@HiltViewModel
class Phase05ViewModel @Inject constructor(
    private val entranceRepo: EntranceRepository,
    private val locationClient: LocationClient,
    private val connectivity: ConnectivityObserver,
    @ApplicationContext private val context: Context,
) : ViewModel() {

    private val _uiState = MutableStateFlow(Phase05UiState())
    val uiState: StateFlow<Phase05UiState> = _uiState.asStateFlow()

    init {
        val hasPermission = ContextCompat.checkSelfPermission(
            context, Manifest.permission.ACCESS_FINE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED
        _uiState.update { it.copy(locationPermission = hasPermission) }

        viewModelScope.launch {
            entranceRepo.entrances.collect { list ->
                _uiState.update { it.copy(entrances = list, isLoading = false) }
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
        refresh()
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

    private fun refresh() = viewModelScope.launch { entranceRepo.refresh() }

    fun captureEntrance() = viewModelScope.launch {
        val s = _uiState.value
        entranceRepo.captureEntrance(s.currentLat, s.currentLng, roadDbId = null)
        _uiState.update { it.copy(captureSuccess = true) }
    }

    fun dismissCaptureSuccess() = _uiState.update { it.copy(captureSuccess = false) }

    fun startEditing(entrance: EntranceEntity) {
        _uiState.update { it.copy(editingEntrance = entrance) }
    }

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
