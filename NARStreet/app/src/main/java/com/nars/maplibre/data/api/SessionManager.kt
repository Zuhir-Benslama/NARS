package com.nars.maplibre.data.api

import com.nars.maplibre.AppPreferences
import com.nars.maplibre.data.model.LoginResponse
import com.nars.maplibre.utils.NarsLogger

class SessionManager(
    private val apiService: ApiService,
    private val appPreferences: AppPreferences
) {
    companion object {
        private const val TAG = "SessionManager"
    }

    fun isLoggedIn(): Boolean = appPreferences.isLoggedIn

    suspend fun login(username: String, password: String): Result<LoginResponse> {
        val result = apiService.login(username, password)
        result.onSuccess { response ->
            apiService.getCookie()?.let { jwtToken ->
                appPreferences.authToken = jwtToken
                apiService.setAuthToken(jwtToken)
            }
            appPreferences.sessionCookie = apiService.getCookie()
            appPreferences.user = response.user.copy(
                username = username,
                name = response.user.name.ifBlank { username }
            )
            appPreferences.municipalityName = response.municipalityName
            NarsLogger.logAuthEvent(TAG, "Session created", username)
        }
        return result
    }

    suspend fun logout() {
        try {
            apiService.logout()
        } catch (e: Exception) {
            NarsLogger.w(TAG, "Logout API call failed: ${e.message}")
        }
        appPreferences.authToken = null
        appPreferences.sessionCookie = null
        appPreferences.user = null
        appPreferences.municipalityName = null
        apiService.setAuthToken(null)
        apiService.setCookie(null)
    }

    fun getUser() = appPreferences.user
    fun getMunicipalityName() = appPreferences.municipalityName
}
