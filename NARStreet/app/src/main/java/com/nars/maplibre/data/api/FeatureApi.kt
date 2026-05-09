package com.nars.maplibre.data.api

import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.utils.Config
import com.nars.maplibre.utils.NarsLogger
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import java.net.HttpURLConnection
import java.net.URL
import javax.net.ssl.HttpsURLConnection

/**
 * Handles feature-related API calls (load, save, update, delete)
 */
class FeatureApi(
    private val baseUrl: String,
    private val json: Json,
    private val tlsSocketFactory: javax.net.ssl.SSLSocketFactory? = null
) {
    companion object {
        private const val TAG = "FeatureApi"
        private const val SAVE_TIMEOUT_MS = 15000
        private const val DEFAULT_TIMEOUT_MS = 10000
    }

    private fun openConnection(url: URL): HttpURLConnection {
        val connection = url.openConnection()
        if (tlsSocketFactory != null && connection is HttpsURLConnection) {
            connection.sslSocketFactory = tlsSocketFactory
        }
        return connection as HttpURLConnection
    }

    /**
     * Load all features for current user
     */
    suspend fun loadFeatures(cookie: String?): Result<List<NarsFeature>> = withContext(Dispatchers.IO) {
        require(baseUrl.startsWith("http://") || baseUrl.startsWith("https://")) { "Invalid API base URL" }

        try {
            val url = URL("$baseUrl/api/load")
            val connection = openConnection(url)
            connection.requestMethod = "GET"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")

            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
            } ?: run {
                NarsLogger.w(TAG, "No auth cookie set! Request will likely fail with 401")
            }

            connection.connectTimeout = Config.API_DEFAULT_TIMEOUT_MS
            connection.readTimeout = Config.API_DEFAULT_TIMEOUT_MS

            val responseCode = connection.responseCode
            NarsLogger.d(TAG, "loadFeatures response code: $responseCode")

            if (responseCode == HttpURLConnection.HTTP_OK) {
                val responseBody = connection.inputStream.bufferedReader().use { it.readText() }
                NarsLogger.d(TAG, "Response length: ${responseBody.length}")

                if (responseBody.isBlank()) {
                    NarsLogger.w(TAG, "Response body is empty!")
                    Result.success(emptyList())
                } else {
                    val features = parseFeaturesFromResponse(responseBody)
                    NarsLogger.d(TAG, "Parsed ${features.size} features")
                    Result.success(features)
                }
            } else if (responseCode == HttpURLConnection.HTTP_UNAUTHORIZED) {
                NarsLogger.w(TAG, "Unauthorized - no valid auth token")
                Result.failure(AuthError(NarsError.ErrorContext(url.toString(), "GET", responseCode)))
            } else {
                NarsLogger.e(TAG, "Load features failed: HTTP $responseCode")
                Result.failure(ServerError(NarsError.ErrorContext(url.toString(), "GET", responseCode)))
            }
        } catch (e: NarsError) {
            NarsLogger.e(TAG, "Load features error: ${e.code}")
            Result.failure(e)
        } catch (e: Exception) {
            NarsLogger.e(TAG, "Load features error")
            Result.failure(NetworkError(NarsError.ErrorContext(baseUrl, "GET"), e))
        }
    }

    /**
     * Save a feature
     */
    suspend fun saveFeature(feature: NarsFeature, cookie: String?): Result<String> = withContext(Dispatchers.IO) {
        try {
            val cleanBaseUrl = baseUrl.trimEnd('/')
            val url = URL("$cleanBaseUrl/api/save")
            val connection = openConnection(url)
            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")
            connection.doOutput = true

            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
            }

            connection.connectTimeout = SAVE_TIMEOUT_MS
            connection.readTimeout = SAVE_TIMEOUT_MS

            val requestBody = buildSaveRequestBody(feature)
            NarsLogger.d(TAG, "Save request body: $requestBody")

            connection.outputStream.use { os ->
                os.write(requestBody.toByteArray())
            }

            val responseCode = connection.responseCode
            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_CREATED) {
                val responseBody = connection.inputStream.bufferedReader().use { it.readText() }
                val responseJson = json.parseToJsonElement(responseBody).jsonObject
                val id = responseJson["id"]?.jsonPrimitive?.contentOrNull
                    ?: responseJson["id"]?.jsonPrimitive?.longOrNull?.toString()
                    ?: feature.id
                Result.success(id)
            } else {
                val errorBody = try {
                    connection.errorStream?.bufferedReader()?.use { it.readText() } ?: ""
                } catch (e: Exception) { "" }
                NarsLogger.e(TAG, "Save failed: HTTP $responseCode, body: $errorBody")
                Result.failure(Exception("Save feature failed: HTTP $responseCode - $errorBody"))
            }
        } catch (e: Exception) {
            NarsLogger.e(TAG, "Save feature error: ${e.message}", e)
            Result.failure(Exception("Save feature error: ${e.message}"))
        }
    }

    /**
     * Update a feature
     */
    suspend fun updateFeature(featureId: String, feature: NarsFeature, cookie: String?): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/update/$featureId")
            val connection = openConnection(url)
            connection.requestMethod = "PUT"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")
            connection.doOutput = true

            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
            }

            connection.connectTimeout = SAVE_TIMEOUT_MS
            connection.readTimeout = SAVE_TIMEOUT_MS

            val requestBody = buildSaveRequestBody(feature)
            connection.outputStream.use { os ->
                os.write(requestBody.toByteArray())
            }

            val responseCode = connection.responseCode
            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_NO_CONTENT) {
                Result.success(Unit)
            } else {
                val errorBody = connection.errorStream?.bufferedReader()?.use { it.readText() } ?: "Unknown error"
                Result.failure(Exception("Update feature failed: HTTP $responseCode - $errorBody"))
            }
        } catch (e: Exception) {
            Result.failure(Exception("Update feature error: ${e.message}"))
        }
    }

    /**
     * Delete a feature
     */
    suspend fun deleteFeature(featureId: String, cookie: String?): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/delete/$featureId")
            val connection = openConnection(url)
            connection.requestMethod = "DELETE"
            connection.setRequestProperty("Accept", "application/json")

            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
            }

            connection.connectTimeout = DEFAULT_TIMEOUT_MS
            connection.readTimeout = DEFAULT_TIMEOUT_MS

            val responseCode = connection.responseCode
            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_NO_CONTENT) {
                Result.success(Unit)
            } else {
                val errorBody = connection.errorStream?.bufferedReader()?.use { it.readText() } ?: "Unknown error"
                Result.failure(Exception("Delete feature failed: HTTP $responseCode - $errorBody"))
            }
        } catch (e: Exception) {
            Result.failure(Exception("Delete feature error: ${e.message}"))
        }
    }

    private fun buildSaveRequestBody(feature: NarsFeature): String = com.nars.maplibre.data.api.buildSaveRequestBody(feature)

    private fun parseFeaturesFromResponse(responseBody: String): List<NarsFeature> = com.nars.maplibre.data.api.parseFeaturesFromResponse(json, responseBody)
}
