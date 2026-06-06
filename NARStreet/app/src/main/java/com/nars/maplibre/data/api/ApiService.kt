package com.nars.maplibre.data.api

import com.nars.maplibre.AppPreferences
import com.nars.maplibre.BuildConfig
import com.nars.maplibre.data.model.LoginApiResponse
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
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put

class ApiService(
    private val httpClient: HttpClient,
    private val preferences: AppPreferences
) {
    companion object {
        private const val TAG = "ApiService"
        private const val LOGIN_TIMEOUT_MS = 15000
    }

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

    suspend fun login(username: String, password: String): Result<LoginResponse> {
        return try {
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
            val apiResponse = apiJson.decodeFromString<LoginApiResponse>(body)

            if (!response.status.isSuccess()) {
                return Result.failure(Exception(apiResponse.message ?: "Login failed: HTTP ${response.status.value}"))
            }

            if (!apiResponse.success) {
                return Result.failure(Exception(apiResponse.message ?: "Login failed"))
            }

            val token = apiResponse.token ?: apiResponse.accessToken
            token?.let { authToken = it }

            val commune = apiResponse.user.commune
            val user = commune?.toUserFields(apiResponse.user) ?: User(
                id = apiResponse.user.id,
                username = apiResponse.user.username,
                name = apiResponse.user.name,
                email = apiResponse.user.email,
                role = apiResponse.user.role
            )

            NarsLogger.logAuthEvent(TAG, "Login successful", username)
            Result.success(LoginResponse(user, token, user.communeName))
        } catch (e: Exception) {
            NarsLogger.e(TAG, "Login failed", e)
            Result.failure(e)
        }
    }

    suspend fun logout(): Result<Unit> {
        return try {
            httpClient.post("$baseUrl/api/logout") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
                contentType(ContentType.Application.Json)
            }
            Result.success(Unit)
        } catch (e: Exception) {
            NarsLogger.e(TAG, "Logout failed", e)
            Result.failure(e)
        }
    }

    suspend fun loadFeatures(): Result<List<NarsFeature>> {
        return try {
            val response = httpClient.get("$baseUrl/api/load") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
            }
            val body = response.bodyAsText()
            if (body.isBlank()) return Result.success(emptyList())
            val jsonElement = apiJson.parseToJsonElement(body)
            val items = if (jsonElement is JsonObject) {
                val apiResponse = apiJson.decodeFromJsonElement(ApiFeatureListResponse.serializer(), jsonElement)
                apiResponse.features ?: emptyList()
            } else {
                apiJson.decodeFromJsonElement(
                    kotlinx.serialization.builtins.ListSerializer(ApiFeatureResponse.serializer()),
                    jsonElement
                )
            }
            Result.success(items.mapNotNull { it.toNarsFeature() })
        } catch (e: Exception) {
            NarsLogger.e(TAG, "loadFeatures failed", e)
            Result.failure(e)
        }
    }

    suspend fun saveFeature(feature: NarsFeature): Result<String> {
        return try {
            val requestBody = apiJson.encodeToString(feature.toSaveFeatureRequest())
            val response = httpClient.post("$baseUrl/api/save") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
                setBody(requestBody)
            }
            val responseBody = response.bodyAsText()
            val responseJson = apiJson.parseToJsonElement(responseBody).jsonObject
            val id = responseJson["id"]?.jsonPrimitive?.contentOrNull
                ?: responseJson["id"]?.jsonPrimitive?.longOrNull?.toString()
                ?: feature.id
            Result.success(id)
        } catch (e: Exception) {
            NarsLogger.e(TAG, "saveFeature failed", e)
            Result.failure(e)
        }
    }

    suspend fun updateFeature(featureId: String, feature: NarsFeature): Result<Unit> {
        return try {
            val requestBody = apiJson.encodeToString(feature.toSaveFeatureRequest())
            httpClient.put("$baseUrl/api/update/$featureId") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
                setBody(requestBody)
            }
            Result.success(Unit)
        } catch (e: Exception) {
            NarsLogger.e(TAG, "updateFeature failed", e)
            Result.failure(e)
        }
    }

    suspend fun deleteFeature(featureId: String): Result<Unit> {
        return try {
            httpClient.delete("$baseUrl/api/delete/$featureId") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
            }
            Result.success(Unit)
        } catch (e: Exception) {
            NarsLogger.e(TAG, "deleteFeature failed", e)
            Result.failure(e)
        }
    }

    suspend fun submitInspection(featureId: String, type: String, data: String, status: String): Result<Unit> {
        return try {
            val requestBody = buildJsonObject {
                put("feature_id", featureId)
                put("type", type)
                put("status", status)
                put("data", apiJson.parseToJsonElement(data))
            }.toString()
            httpClient.post("$baseUrl/api/field/inspect") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
                contentType(ContentType.Application.Json)
                setBody(requestBody)
            }
            Result.success(Unit)
        } catch (e: Exception) {
            NarsLogger.e(TAG, "submitInspection failed", e)
            Result.failure(e)
        }
    }

    suspend fun createEntranceFromInspection(roadId: String, data: String, label: String = "Entrance (field worker)"): Result<String> {
        return try {
            val requestBody = buildJsonObject {
                put("road_id", roadId)
                put("label", label)
            }.toString()
            val response = httpClient.post("$baseUrl/api/field/entrance/create") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
                contentType(ContentType.Application.Json)
                setBody(requestBody)
            }
            val body = response.bodyAsText()
            val jsonElement = apiJson.parseToJsonElement(body).jsonObject
            val id = jsonElement["id"]?.jsonPrimitive?.contentOrNull
                ?: return Result.failure(Exception("No ID in response"))
            Result.success(id)
        } catch (e: Exception) {
            NarsLogger.e(TAG, "createEntranceFromInspection failed", e)
            Result.failure(e)
        }
    }

    suspend fun loadFieldFeatures(type: String): Result<String> {
        return try {
            val response = httpClient.get("$baseUrl/api/field/features?type=$type") {
                authHeaders().forEach { (k, v) -> headers.append(k, v) }
            }
            Result.success(response.bodyAsText())
        } catch (e: Exception) {
            NarsLogger.e(TAG, "loadFieldFeatures failed", e)
            Result.failure(e)
        }
    }
}
