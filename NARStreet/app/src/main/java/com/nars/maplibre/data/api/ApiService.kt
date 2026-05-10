package com.nars.maplibre.data.api

import com.nars.maplibre.AppPreferences
import com.nars.maplibre.BuildConfig
import com.nars.maplibre.data.model.LoginRequest
import com.nars.maplibre.data.model.LoginResponse
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.User
import com.nars.maplibre.utils.NarsLogger
import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.request.delete
import io.ktor.client.request.get
import io.ktor.client.request.post
import io.ktor.client.request.put
import io.ktor.client.request.setBody
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

class ApiService(
    private val httpClient: HttpClient,
    private val preferences: AppPreferences
) {
    companion object {
        private const val TAG = "ApiService"
        private const val LOGIN_TIMEOUT_MS = 15000
    }

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }
    private val baseUrl: String = BuildConfig.API_BASE_URL.trimEnd('/')

    @Volatile private var authToken: String? = null
    @Volatile private var cookie: String? = null

    fun setAuthToken(token: String?) {
        authToken = token
    }

    fun getAuthToken(): String? = authToken

    fun setCookie(c: String?) {
        cookie = c
    }

    fun getCookie(): String? = cookie

    private fun authHeaders(): Map<String, String> {
        val headers = mutableMapOf<String, String>()
        authToken?.let { headers["Authorization"] = "Bearer $it" }
        cookie?.let { headers["Cookie"] = it }
        return headers
    }

    suspend fun login(username: String, password: String): Result<LoginResponse> = runCatching {
        val response = httpClient.post("$baseUrl/api/signin") {
            contentType(ContentType.Application.Json)
            setBody(LoginRequest(username, password))
        }

        val cookieHeader = response.headers[HttpHeaders.SetCookie]
        cookieHeader?.let { rawCookie ->
            val tokenMatch = Regex("access_token=([^;]+)").find(rawCookie)
            tokenMatch?.let { match ->
                cookie = match.groupValues[1]
            }
        }

        val body = response.bodyAsText()
        val jsonElement = json.parseToJsonElement(body)

        if (!response.status.isSuccess()) {
            val errorMessage = jsonElement.jsonObject["message"]?.jsonPrimitive?.contentOrNull
                ?: "Login failed: HTTP ${response.status.value}"
            throw Exception(errorMessage)
        }

        val success = jsonElement.jsonObject["success"]?.jsonPrimitive?.booleanOrNull ?: true
        if (!success) {
            val errorMessage = jsonElement.jsonObject["message"]?.jsonPrimitive?.contentOrNull
                ?: "Login failed"
            throw Exception(errorMessage)
        }

        val userObj = jsonElement.jsonObject["user"]?.jsonObject ?: JsonObject(emptyMap())
        val userId = userObj["id"]?.jsonPrimitive?.contentOrNull ?: ""
        val apiUsername = userObj["username"]?.jsonPrimitive?.content ?: username
        val apiName = userObj["name"]?.jsonPrimitive?.content ?: username
        val email = userObj["email"]?.jsonPrimitive?.contentOrNull

        val communeObj = userObj["commune"]?.jsonObject ?: JsonObject(emptyMap())
        val communeLat = communeObj["latitude"]?.jsonPrimitive?.doubleOrNull
        val communeLng = communeObj["longitude"]?.jsonPrimitive?.doubleOrNull
        val communeName = communeObj["name_fr"]?.jsonPrimitive?.contentOrNull

        val user = User(
            id = userId,
            username = apiUsername,
            name = apiName,
            email = email,
            communeLatitude = communeLat,
            communeLongitude = communeLng,
            communeName = communeName
        )

        val token = jsonElement.jsonObject["token"]?.jsonPrimitive?.contentOrNull
            ?: jsonElement.jsonObject["accessToken"]?.jsonPrimitive?.contentOrNull
        token?.let { authToken = it }

        NarsLogger.logAuthEvent(TAG, "Login successful", username)
        LoginResponse(user, token, communeName)
    }

    suspend fun getCurrentUser(): Result<User> = runCatching {
        val response = httpClient.get("$baseUrl/api/current_user") {
            authHeaders().forEach { (k, v) -> headers.append(k, v) }
        }
        val body = response.bodyAsText()
        val jsonElement = json.parseToJsonElement(body)

        if (!response.status.isSuccess()) {
            val errorMessage = jsonElement.jsonObject["message"]?.jsonPrimitive?.contentOrNull
                ?: "Request failed: HTTP ${response.status.value}"
            throw Exception(errorMessage)
        }

        val userId = jsonElement.jsonObject["id"]?.jsonPrimitive?.contentOrNull ?: ""
        val apiUsername = jsonElement.jsonObject["username"]?.jsonPrimitive?.content ?: ""
        val apiName = jsonElement.jsonObject["name"]?.jsonPrimitive?.content ?: ""
        val email = jsonElement.jsonObject["email"]?.jsonPrimitive?.contentOrNull

        val communeObj = jsonElement.jsonObject["commune"]?.jsonObject ?: JsonObject(emptyMap())
        val communeLat = communeObj["latitude"]?.jsonPrimitive?.doubleOrNull
        val communeLng = communeObj["longitude"]?.jsonPrimitive?.doubleOrNull
        val communeName = communeObj["name_fr"]?.jsonPrimitive?.contentOrNull

        User(
            id = userId,
            username = apiUsername,
            name = apiName,
            email = email,
            communeLatitude = communeLat,
            communeLongitude = communeLng,
            communeName = communeName
        )
    }

    suspend fun logout(): Result<Unit> = runCatching {
        httpClient.post("$baseUrl/api/logout") {
            authHeaders().forEach { (k, v) -> headers.append(k, v) }
            contentType(ContentType.Application.Json)
        }
        Unit
    }

    suspend fun refreshToken(): Result<Unit> = runCatching {
        val response = httpClient.post("$baseUrl/api/refresh") {
            contentType(ContentType.Application.Json)
            cookie?.let { headers.append(HttpHeaders.Cookie, it) }
        }

        val cookieHeader = response.headers[HttpHeaders.SetCookie]
        cookieHeader?.let { rawCookie ->
            val tokenMatch = Regex("access_token=([^;]+)").find(rawCookie)
            tokenMatch?.let { match ->
                authToken = match.groupValues[1]
                cookie = rawCookie
            }
        }
        Unit
    }

    suspend fun isAuthenticated(): Boolean = getCurrentUser().isSuccess

    suspend fun loadFeatures(): Result<List<NarsFeature>> = runCatching {
        val response = httpClient.get("$baseUrl/api/load") {
            authHeaders().forEach { (k, v) -> headers.append(k, v) }
        }
        val body = response.bodyAsText()
        if (body.isBlank()) return@runCatching emptyList()
        parseFeaturesFromResponse(json, body)
    }

    suspend fun saveFeature(feature: NarsFeature): Result<String> = runCatching {
        val requestBody = buildSaveRequestBody(feature)
        val response = httpClient.post("$baseUrl/api/save") {
            authHeaders().forEach { (k, v) -> headers.append(k, v) }
            setBody(requestBody)
        }
        val responseBody = response.bodyAsText()
        val responseJson = json.parseToJsonElement(responseBody).jsonObject
        responseJson["id"]?.jsonPrimitive?.contentOrNull
            ?: responseJson["id"]?.jsonPrimitive?.longOrNull?.toString()
            ?: feature.id
    }

    suspend fun updateFeature(featureId: String, feature: NarsFeature): Result<Unit> = runCatching {
        val requestBody = buildSaveRequestBody(feature)
        httpClient.put("$baseUrl/api/update/$featureId") {
            authHeaders().forEach { (k, v) -> headers.append(k, v) }
            setBody(requestBody)
        }
        Unit
    }

    suspend fun deleteFeature(featureId: String): Result<Unit> = runCatching {
        httpClient.delete("$baseUrl/api/delete/$featureId") {
            authHeaders().forEach { (k, v) -> headers.append(k, v) }
        }
        Unit
    }
}
