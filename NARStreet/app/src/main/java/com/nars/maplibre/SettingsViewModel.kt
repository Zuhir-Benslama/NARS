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

    fun setThemeMode(mode: ThemeMode) {
        _themeMode.value = mode
        appPreferences.themeMode = mode
    }
}
