package com.nars.narstreet.core.session

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.stateIn
import org.maplibre.android.geometry.LatLng
import javax.inject.Inject
import javax.inject.Singleton

private val Context.dataStore by preferencesDataStore(name = "nars_session")

@Singleton
class SessionManager @Inject constructor(
    @ApplicationContext private val context: Context,
) {
    companion object {
        private val KEY_TOKEN    = stringPreferencesKey("access_token")
        private val KEY_USERNAME = stringPreferencesKey("username")
        private val KEY_COMMUNE  = stringPreferencesKey("commune_name")
        private val KEY_COMM_LAT = stringPreferencesKey("commune_lat")
        private val KEY_COMM_LNG = stringPreferencesKey("commune_lng")
    }

    // Long-lived scope tied to the singleton — survives the whole app process.
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    // Eagerly-started StateFlow: DataStore is read once on startup and kept hot.
    // AuthInterceptor can call tokenState.value synchronously — no runBlocking needed.
    val tokenState: StateFlow<String?> = context.dataStore.data
        .map { it[KEY_TOKEN] }
        .stateIn(scope, SharingStarted.Eagerly, initialValue = null)

    // Also eagerly started so the map camera center is available immediately
    // when any composable first renders — no race between DataStore read and
    // MapLibre style initialization.
    val communeCenterState: StateFlow<LatLng> = context.dataStore.data
        .map { prefs ->
            val lat = prefs[KEY_COMM_LAT]?.toDoubleOrNull() ?: 36.7
            val lng = prefs[KEY_COMM_LNG]?.toDoubleOrNull() ?: 3.05
            LatLng(lat, lng)
        }
        .stateIn(scope, SharingStarted.Eagerly, initialValue = LatLng(36.7, 3.05))

    val token: Flow<String?>       = context.dataStore.data.map { it[KEY_TOKEN] }
    val username: Flow<String?>    = context.dataStore.data.map { it[KEY_USERNAME] }
    val communeName: Flow<String?> = context.dataStore.data.map { it[KEY_COMMUNE] }

    // communeCenter delegates to the eagerly-started StateFlow so all collectors
    // get the persisted value immediately without waiting for a DataStore read.
    val communeCenter: Flow<LatLng> = communeCenterState

    suspend fun save(
        token: String,
        username: String,
        communeName: String,
        communeLat: Double = 36.7,
        communeLng: Double = 3.05,
    ) {
        context.dataStore.edit {
            it[KEY_TOKEN]    = token
            it[KEY_USERNAME] = username
            it[KEY_COMMUNE]  = communeName
            it[KEY_COMM_LAT] = communeLat.toString()
            it[KEY_COMM_LNG] = communeLng.toString()
        }
    }

    suspend fun clear() { context.dataStore.edit { it.clear() } }

    suspend fun currentToken(): String?  = token.firstOrNull()
    suspend fun currentCommuneCenter(): LatLng = communeCenter.firstOrNull() ?: LatLng(36.7, 3.05)
}
