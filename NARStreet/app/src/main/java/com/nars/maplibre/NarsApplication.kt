package com.nars.maplibre

import android.app.Application
import android.content.Context
import com.nars.maplibre.data.api.ApiClient
import com.nars.maplibre.data.store.FeatureStore
import com.nars.maplibre.utils.NarsLogger
import com.nars.maplibre.utils.TlsUtils
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import org.maplibre.android.MapLibre
import org.maplibre.android.WellKnownTileServer

class NarsApplication : Application() {

    val featureStore: FeatureStore by lazy { FeatureStore() }

    val appPreferences: AppPreferences by lazy { AppPreferences(this) }

    val apiClient: ApiClient by lazy {
        ApiClient(tlsSocketFactory = TlsUtils.getSocketFactory(this))
    }

    private val applicationScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        instance = this

        MapLibre.getInstance(this, null, WellKnownTileServer.MapTiler)

        appPreferences.authToken?.let { jwtToken ->
            apiClient.setAuthToken(jwtToken)
            apiClient.setCookie(jwtToken)
            applicationScope.launch {
                val refreshResult = apiClient.refreshToken()
                if (refreshResult.isSuccess) {
                    val newToken = apiClient.getAuthToken()
                    if (newToken != null && newToken != jwtToken) {
                        appPreferences.authToken = newToken
                        appPreferences.sessionCookie = apiClient.getCookie()
                    }
                    NarsLogger.d("NarsApplication", "Token refreshed on startup")
                }
            }
        }
    }

    fun isLoggedIn(): Boolean = appPreferences.isLoggedIn

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
