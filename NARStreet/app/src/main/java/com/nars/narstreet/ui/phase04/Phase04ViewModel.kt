package com.nars.narstreet.ui.phase04

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.RoadEntity
import com.nars.narstreet.data.model.SyncStatus
import com.nars.narstreet.repository.AuthRepository
import com.nars.narstreet.repository.RoadRepository
import com.nars.narstreet.ui.components.SyncState
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject

data class Phase04UiState(
    val roads: List<RoadEntity>  = emptyList(),
    val isLoading: Boolean       = true,
    val syncState: SyncState     = SyncState.IDLE,
    val editingRoad: RoadEntity? = null,
)

@HiltViewModel
class Phase04ViewModel @Inject constructor(
    private val roadRepo: RoadRepository,
    private val authRepo: AuthRepository,
    private val connectivity: ConnectivityObserver,
) : ViewModel() {

    private val _uiState = MutableStateFlow(Phase04UiState())
    val uiState: StateFlow<Phase04UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch {
            roadRepo.roads.collect { roads ->
                _uiState.update { it.copy(roads = roads, isLoading = false) }
            }
        }
        viewModelScope.launch {
            combine(roadRepo.pendingCount, connectivity.isOnline) { pending, online ->
                when {
                    pending > 0 && !online -> SyncState.OFFLINE
                    pending > 0 && online  -> SyncState.PENDING
                    else                   -> SyncState.IDLE
                }
            }.collect { syncState -> _uiState.update { it.copy(syncState = syncState) } }
        }
        refresh()
    }

    private fun refresh() = viewModelScope.launch {
        roadRepo.refresh()
    }

    fun startEditing(road: RoadEntity) {
        _uiState.update { it.copy(editingRoad = road) }
    }

    fun cancelEditing() {
        _uiState.update { it.copy(editingRoad = null) }
    }

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
            val updated = road.copy(
                lanes           = lanes,
                trafficCapacity = traffic,
                tradActivity    = trad,
                hasMedianStrip  = median,
                hasGreenery     = greenery,
                isDeadEnd       = deadEnd,
                syncStatus      = SyncStatus.PENDING,
            )
            roadRepo.saveCharacteristics(updated)
            _uiState.update { it.copy(editingRoad = null) }
        }
    }

    fun logout(onDone: () -> Unit) = viewModelScope.launch {
        authRepo.logout()
        onDone()
    }
}
