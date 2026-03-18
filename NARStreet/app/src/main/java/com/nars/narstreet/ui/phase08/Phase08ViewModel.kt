package com.nars.narstreet.ui.phase08

import androidx.lifecycle.SavedStateHandle
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.nars.narstreet.core.sync.ConnectivityObserver
import com.nars.narstreet.data.model.PanelEntity
import com.nars.narstreet.repository.PanelRepository
import com.nars.narstreet.ui.components.SyncState
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject

data class Phase08UiState(
    val panel: PanelEntity?  = null,
    val isLoading: Boolean   = true,
    val isSaved: Boolean     = false,
    val syncState: SyncState = SyncState.IDLE,
)

@HiltViewModel
class Phase08ViewModel @Inject constructor(
    savedState: SavedStateHandle,
    private val repo: PanelRepository,
    private val connectivity: ConnectivityObserver,
) : ViewModel() {

    private val panelId: Long = savedState["panelId"] ?: 0L

    private val _uiState = MutableStateFlow(Phase08UiState())
    val uiState: StateFlow<Phase08UiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch {
            repo.panels.collect { list ->
                val panel = list.find { it.id == panelId }
                _uiState.update { it.copy(panel = panel, isLoading = false) }
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

    fun saveChecklist(
        isPlaced: Boolean,
        isCorrectLocation: Boolean?,
    ) {
        val panel = _uiState.value.panel ?: return
        viewModelScope.launch {
            repo.saveChecklist(
                panel.copy(
                    isPlaced          = isPlaced,
                    isCorrectLocation = isCorrectLocation,
                    orderNeeded       = !isPlaced,
                    relocateNeeded    = isPlaced && isCorrectLocation == false,
                )
            )
            _uiState.update { it.copy(isSaved = true) }
        }
    }
}
