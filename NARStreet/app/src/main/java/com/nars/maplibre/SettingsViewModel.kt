package com.nars.maplibre

import androidx.lifecycle.ViewModel
import com.nars.maplibre.ui.theme.ThemeMode
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

class SettingsViewModel(
    private val appPreferences: AppPreferences
) : ViewModel() {

    private val _themeMode = MutableStateFlow(appPreferences.themeMode)
    val themeMode: StateFlow<ThemeMode> = _themeMode.asStateFlow()

    private val _snapDistance = MutableStateFlow(appPreferences.snapDistance)
    val snapDistance: StateFlow<Float> = _snapDistance.asStateFlow()

    private val _showGrid = MutableStateFlow(appPreferences.showGrid)
    val showGrid: StateFlow<Boolean> = _showGrid.asStateFlow()

    private val _showLabels = MutableStateFlow(appPreferences.showLabels)
    val showLabels: StateFlow<Boolean> = _showLabels.asStateFlow()

    fun setThemeMode(mode: ThemeMode) {
        _themeMode.value = mode
        appPreferences.themeMode = mode
    }

    fun setSnapDistance(distance: Float) {
        _snapDistance.value = distance
        appPreferences.snapDistance = distance
    }

    fun setShowGrid(enabled: Boolean) {
        _showGrid.value = enabled
        appPreferences.showGrid = enabled
    }

    fun setShowLabels(enabled: Boolean) {
        _showLabels.value = enabled
        appPreferences.showLabels = enabled
    }
}
