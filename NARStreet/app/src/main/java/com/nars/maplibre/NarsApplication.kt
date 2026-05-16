package com.nars.maplibre

import android.app.Application
import com.nars.maplibre.di.appModule
import com.nars.maplibre.utils.NarsLogger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import org.koin.android.ext.koin.androidContext
import org.koin.core.context.startKoin
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
                val prefs: AppPreferences = org.koin.java.KoinJavaComponent.get(AppPreferences::class.java)
                prefs.authToken?.let { jwtToken ->
                    NarsLogger.d("NarsApplication", "User session found on startup")
                }
            } catch (e: Exception) {
                NarsLogger.w("NarsApplication", "Session check skipped: ${e.message}")
            }
        }
    }

    override fun onTerminate() {
        applicationScope.cancel()
        super.onTerminate()
    }
}
