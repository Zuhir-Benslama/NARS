package com.nars.maplibre

import android.app.Application
import android.content.Context
import com.nars.maplibre.data.api.ApiClient
import com.nars.maplibre.data.store.FeatureStore
import org.maplibre.android.MapLibre
import org.maplibre.android.WellKnownTileServer

/**
 * NARS Application class
 * Global application state and dependencies
 */
class NarsApplication : Application() {

    // Global feature store
    val featureStore: FeatureStore by lazy { FeatureStore() }

    // App preferences
    val appPreferences: AppPreferences by lazy { AppPreferences(this) }

    // API Client
    val apiClient: ApiClient by lazy { ApiClient() }

    override fun onCreate() {
        super.onCreate()
        instance = this

        // Initialize MapLibre SDK before any MapView is created
        MapLibre.getInstance(this, null, WellKnownTileServer.MapTiler)

        // Restore JWT token to API client (stored from login cookie)
        appPreferences.authToken?.let { jwtToken ->
            apiClient.setAuthToken(jwtToken)
            apiClient.setCookie(jwtToken) // Also set as cookie for API calls
        }
    }

    /**
     * Check if user is logged in
     */
    fun isLoggedIn(): Boolean = appPreferences.isLoggedIn

    /**
     * Logout user
     */
    suspend fun logout() {
        apiClient.logout()
        appPreferences.authToken = null
        appPreferences.sessionCookie = null
        appPreferences.user = null
        appPreferences.municipalityName = null
    }

    companion object {
        lateinit var instance: NarsApplication
            private set

        val context: Context
            get() = instance.applicationContext
    }
}
