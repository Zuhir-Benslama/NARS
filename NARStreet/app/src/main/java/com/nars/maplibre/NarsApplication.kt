package com.nars.maplibre

import android.app.Application
import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.di.appModule
import com.nars.maplibre.utils.NarsLogger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import org.koin.android.ext.koin.androidContext
import org.koin.core.context.startKoin
import org.koin.java.KoinJavaComponent.get
import org.maplibre.android.MapLibre
import org.maplibre.android.WellKnownTileServer

class NarsApplication : Application() {

    private val applicationScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        startKoin {
            androidContext(this@NarsApplication)
            modules(appModule)
        }
        MapLibre.getInstance(this, null, WellKnownTileServer.MapTiler)

        applicationScope.launch {
            try {
                val prefs: AppPreferences = get(AppPreferences::class.java)
                val apiService: ApiService = get(ApiService::class.java)

                prefs.authToken?.let { jwtToken ->
                    apiService.setAuthToken(jwtToken)
                    apiService.setCookie(jwtToken)
                    val refreshResult = apiService.refreshToken()
                    if (refreshResult.isSuccess) {
                        val newToken = apiService.getAuthToken()
                        if (newToken != null && newToken != jwtToken) {
                            prefs.authToken = newToken
                            prefs.sessionCookie = apiService.getCookie()
                        }
                        NarsLogger.d("NarsApplication", "Token refreshed on startup")
                    }
                }
            } catch (e: Exception) {
                NarsLogger.w("NarsApplication", "Token refresh skipped: ${e.message}")
            }
        }
    }

    fun isLoggedIn(): Boolean {
        return try {
            get<AppPreferences>(AppPreferences::class.java).isLoggedIn
        } catch (e: Exception) {
            false
        }
    }

    suspend fun logout() {
        try {
            val prefs: AppPreferences = get(AppPreferences::class.java)
            val apiService: ApiService = get(ApiService::class.java)
            try { apiService.logout() } catch (_: Exception) {}
            prefs.authToken = null
            prefs.sessionCookie = null
            prefs.user = null
            prefs.municipalityName = null
        } catch (_: Exception) {}
    }
}
