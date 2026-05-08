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

/**
 * Application preferences using SharedPreferences
 * Sensitive data (auth tokens, user info) is stored in EncryptedSharedPreferences
 */
class AppPreferences(context: Context) {

    private val prefs: SharedPreferences = context.getSharedPreferences(
        PREFS_NAME, Context.MODE_PRIVATE
    )
    
    private val securePrefs: SecurePreferences = SecurePreferences(context)
    private val json = Json { ignoreUnknownKeys = true }

    // Current theme mode
    var themeMode: ThemeMode
        get() = ThemeMode.valueOf(prefs.getString(KEY_THEME, ThemeMode.AUTO.name)!!)
        set(value) = prefs.edit { putString(KEY_THEME, value.name) }

    // Current base layer
    var baseLayer: BaseLayerType
        get() = BaseLayerType.valueOf(prefs.getString(KEY_BASE_LAYER, BaseLayerType.SATELLITE.name)!!)
        set(value) = prefs.edit { putString(KEY_BASE_LAYER, value.name) }

    // Snap distance in meters
    var snapDistance: Float
        get() = prefs.getFloat(KEY_SNAP_DISTANCE, DEFAULT_SNAP_DISTANCE)
        set(value) = prefs.edit { putFloat(KEY_SNAP_DISTANCE, value) }

    // Current phase key
    var currentPhase: String?
        get() = prefs.getString(KEY_CURRENT_PHASE, null)
        set(value) = prefs.edit { putString(KEY_CURRENT_PHASE, value) }

    // Show grid
    var showGrid: Boolean
        get() = prefs.getBoolean(KEY_SHOW_GRID, false)
        set(value) = prefs.edit { putBoolean(KEY_SHOW_GRID, value) }

    // Show labels
    var showLabels: Boolean
        get() = prefs.getBoolean(KEY_SHOW_LABELS, true)
        set(value) = prefs.edit { putBoolean(KEY_SHOW_LABELS, value) }

    // Auth token - stored in EncryptedSharedPreferences
    var authToken: String?
        get() = securePrefs.getAuthToken()
        set(value) {
            if (value != null) {
                securePrefs.saveAuthToken(value)
            } else {
                securePrefs.clearAuthToken()
            }
        }

    // Session cookie - stored in EncryptedSharedPreferences
    var sessionCookie: String?
        get() = securePrefs.getCookie()
        set(value) {
            if (value != null) {
                securePrefs.saveCookie(value)
            } else {
                securePrefs.clearCookie()
            }
        }

    // Logged in user - stored in EncryptedSharedPreferences
    var user: User?
        get() = securePrefs.getUser()
        set(value) {
            if (value != null) {
                securePrefs.saveUser(value)
            } else {
                securePrefs.clearUser()
            }
        }

    // Municipality name - stored in EncryptedSharedPreferences
    var municipalityName: String?
        get() = securePrefs.getMunicipalityName()
        set(value) {
            if (value != null) {
                securePrefs.saveMunicipalityName(value)
            } else {
                securePrefs.clearMunicipalityName()
            }
        }

    // Is logged in
    val isLoggedIn: Boolean
        get() = user != null && authToken != null

    companion object {
        private const val PREFS_NAME = "nars_preferences"

        private const val KEY_THEME = "theme"
        private const val KEY_BASE_LAYER = "base_layer"
        private const val KEY_SNAP_DISTANCE = "snap_distance"
        private const val KEY_CURRENT_PHASE = "current_phase"
        private const val KEY_SHOW_GRID = "show_grid"
        private const val KEY_SHOW_LABELS = "show_labels"
        private const val KEY_AUTH_TOKEN = "auth_token"
        private const val KEY_USER = "user"
        private const val KEY_MUNICIPALITY = "municipality"

        private const val DEFAULT_SNAP_DISTANCE = 20f // meters
    }
}
