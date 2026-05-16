package com.nars.maplibre

import android.content.Context
import android.content.SharedPreferences
import androidx.core.content.edit
import com.nars.maplibre.data.model.BaseLayerType
import com.nars.maplibre.data.model.User
import com.nars.maplibre.security.SecurePreferences
import com.nars.maplibre.ui.theme.ThemeMode
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

class AppPreferences(context: Context) {

    private val prefs: SharedPreferences = context.getSharedPreferences(
        PREFS_NAME, Context.MODE_PRIVATE
    )

    private val securePrefs: SecurePreferences = SecurePreferences(context)
    private val json = Json { ignoreUnknownKeys = true }

    var themeMode: ThemeMode
        get() = runCatching { ThemeMode.valueOf(prefs.getString(KEY_THEME, ThemeMode.AUTO.name) ?: ThemeMode.AUTO.name) }
            .getOrDefault(ThemeMode.AUTO)
        set(value) = prefs.edit { putString(KEY_THEME, value.name) }

    var baseLayer: BaseLayerType
        get() = runCatching { BaseLayerType.valueOf(prefs.getString(KEY_BASE_LAYER, BaseLayerType.SATELLITE.name) ?: BaseLayerType.SATELLITE.name) }
            .getOrDefault(BaseLayerType.SATELLITE)
        set(value) = prefs.edit { putString(KEY_BASE_LAYER, value.name) }

    var currentPhase: String?
        get() = prefs.getString(KEY_CURRENT_PHASE, null)
        set(value) = prefs.edit { putString(KEY_CURRENT_PHASE, value) }

    var authToken: String?
        get() = securePrefs.getAuthToken()
        set(value) {
            if (value != null) {
                securePrefs.saveAuthToken(value)
            } else {
                securePrefs.clearAuthToken()
            }
        }

    var sessionCookie: String?
        get() = securePrefs.getCookie()
        set(value) {
            if (value != null) {
                securePrefs.saveCookie(value)
            } else {
                securePrefs.clearCookie()
            }
        }

    var user: User?
        get() = securePrefs.getUser()
        set(value) {
            if (value != null) {
                securePrefs.saveUser(value)
            } else {
                securePrefs.clearUser()
            }
        }

    var municipalityName: String?
        get() = securePrefs.getMunicipalityName()
        set(value) {
            if (value != null) {
                securePrefs.saveMunicipalityName(value)
            } else {
                securePrefs.clearMunicipalityName()
            }
        }

    val isLoggedIn: Boolean
        get() = user != null && authToken != null

    companion object {
        private const val PREFS_NAME = "nars_preferences"

        private const val KEY_THEME = "theme"
        private const val KEY_BASE_LAYER = "base_layer"
        private const val KEY_CURRENT_PHASE = "current_phase"
    }
}
