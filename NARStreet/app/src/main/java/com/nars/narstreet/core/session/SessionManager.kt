package com.nars.narstreet.core.session

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.firstOrNull
import kotlinx.coroutines.flow.map
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
    }

    val token: Flow<String?>       = context.dataStore.data.map { it[KEY_TOKEN] }
    val username: Flow<String?>    = context.dataStore.data.map { it[KEY_USERNAME] }
    val communeName: Flow<String?> = context.dataStore.data.map { it[KEY_COMMUNE] }

    suspend fun save(token: String, username: String, communeName: String) {
        context.dataStore.edit {
            it[KEY_TOKEN]    = token
            it[KEY_USERNAME] = username
            it[KEY_COMMUNE]  = communeName
        }
    }

    suspend fun clear() {
        context.dataStore.edit { it.clear() }
    }

    /** Suspend read — safe to call from coroutine context (e.g. AuthInterceptor). */
    suspend fun currentToken(): String? = token.firstOrNull()
}
