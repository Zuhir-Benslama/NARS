package com.nars.maplibre.data.api

import com.nars.maplibre.BuildConfig
import com.nars.maplibre.data.model.LoginResponse
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.User
import kotlinx.serialization.json.Json
import javax.net.ssl.SSLSocketFactory

class ApiClient(
    private val baseUrl: String = BuildConfig.API_BASE_URL,
    private val tlsSocketFactory: SSLSocketFactory? = null
) {
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    private var authToken: String? = null
    private var cookie: String? = null

    val authApi = AuthApi(baseUrl, json, authToken, cookie, tlsSocketFactory)
    val featureApi = FeatureApi(baseUrl, json, tlsSocketFactory)

    fun setAuthToken(token: String?) {
        authToken = token
        authApi.setAuthToken(token)
    }

    fun getAuthToken(): String? = authToken

    fun setCookie(cookie: String?) {
        this.cookie = cookie
        authApi.setCookie(cookie)
    }

    fun getCookie(): String? = cookie

    suspend fun login(username: String, password: String): Result<LoginResponse> {
        val result = authApi.login(username, password)
        if (result.isSuccess) {
            cookie?.let { setCookie(it) }
        }
        return result
    }

    suspend fun loadFeatures(): Result<List<NarsFeature>> = featureApi.loadFeatures(cookie)

    suspend fun saveFeature(feature: NarsFeature): Result<String> = featureApi.saveFeature(feature, cookie)

    suspend fun deleteFeature(featureId: String): Result<Unit> = featureApi.deleteFeature(featureId, cookie)

    suspend fun updateFeature(featureId: String, feature: NarsFeature): Result<Unit> =
        featureApi.updateFeature(featureId, feature, cookie)

    suspend fun getCurrentUser(): Result<User> = authApi.getCurrentUser(cookie)

    suspend fun logout(): Result<Unit> = authApi.logout(cookie)

    suspend fun refreshToken(): Result<Unit> = authApi.refreshToken()

    suspend fun isAuthenticated(): Boolean = authApi.isAuthenticated(cookie)
}
