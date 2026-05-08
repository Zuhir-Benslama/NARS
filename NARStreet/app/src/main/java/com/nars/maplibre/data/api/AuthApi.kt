package com.nars.maplibre.data.api

import com.nars.maplibre.BuildConfig
import com.nars.maplibre.data.model.LoginRequest
import com.nars.maplibre.data.model.LoginResponse
import com.nars.maplibre.data.model.User
import com.nars.maplibre.utils.NarsLogger
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.net.HttpURLConnection
import java.net.URL

/**
 * Handles authentication-related API calls
 */
class AuthApi(
    private val baseUrl: String,
    private val json: Json,
    private var authToken: String?,
    private var cookie: String?
) {
    companion object {
        private const val TAG = "AuthApi"
        private const val MAX_USERNAME_LENGTH = 100
        private const val MAX_PASSWORD_LENGTH = 200
        private const val LOGIN_TIMEOUT_MS = 15000
        private const val DEFAULT_TIMEOUT_MS = 10000
        private const val HTTP_REDIRECT_FOUND = 302
        private const val HTTP_REDIRECT_SEE_OTHER = 303
    }

    fun setAuthToken(token: String?) {
        authToken = token
    }

    fun setCookie(cookie: String?) {
        this.cookie = cookie
    }

    /**
     * Login with username and password
     */
    suspend fun login(username: String, password: String): Result<LoginResponse> = withContext(Dispatchers.IO) {
        require(username.isNotBlank()) { "Username cannot be blank" }
        require(password.isNotBlank()) { "Password cannot be blank" }
        require(username.length <= MAX_USERNAME_LENGTH) { "Username too long (max 100 characters)" }
        require(password.length <= MAX_PASSWORD_LENGTH) { "Password too long (max 200 characters)" }
        require(baseUrl.startsWith("http://") || baseUrl.startsWith("https://")) { "Invalid API base URL" }

        NarsLogger.logAuthEvent(TAG, "Login attempt", username)

        try {
            val url = URL("$baseUrl/api/signin")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")
            connection.doOutput = true
            connection.connectTimeout = LOGIN_TIMEOUT_MS
            connection.readTimeout = LOGIN_TIMEOUT_MS
            connection.instanceFollowRedirects = false

            val requestBody = json.encodeToString(LoginRequest.serializer(), LoginRequest(username, password))
            connection.outputStream.use { os ->
                os.write(requestBody.toByteArray())
            }

            val responseCode = connection.responseCode

            // Extract token from cookie
            val cookies = connection.headerFields["Set-Cookie"]
            cookies?.firstOrNull()?.let { cookieValue ->
                val tokenMatch = Regex("access_token=([^;]+)").find(cookieValue)
                tokenMatch?.let { match ->
                    cookie = match.groupValues[1]
                }
            }

            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_CREATED) {
                val responseBody = connection.inputStream.bufferedReader().use { it.readText() }

                return@withContext try {
                    val jsonElement = json.parseToJsonElement(responseBody)
                    val success = jsonElement.jsonObject["success"]?.jsonPrimitive?.booleanOrNull ?: true

                    if (!success) {
                        val errorMessage = jsonElement.jsonObject["message"]?.jsonPrimitive?.contentOrNull
                            ?: "Login failed"
                        return@withContext Result.failure(Exception(errorMessage))
                    }

                    val userObj = jsonElement.jsonObject["user"]?.jsonObject ?: JsonObject(emptyMap())
                    val userId = userObj["id"]?.jsonPrimitive?.intOrNull ?: 0
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
                    Result.success(LoginResponse(user, token, communeName))
                } catch (e: Exception) {
                    NarsLogger.w(TAG, "Login response parse error: ${e.message}")
                    Result.success(LoginResponse(
                        user = User(id = 0, username = username, name = username),
                        token = null,
                        municipalityName = null
                    ))
                }
            } else if (responseCode == HTTP_REDIRECT_FOUND || responseCode == HTTP_REDIRECT_SEE_OTHER) {
                NarsLogger.logAuthEvent(TAG, "Login redirect - session-based auth", username)
                return@withContext Result.success(LoginResponse(
                    user = User(id = 0, username = username, name = username),
                    token = null,
                    municipalityName = null
                ))
            } else {
                val errorBody = connection.errorStream?.bufferedReader()?.use { it.readText() }
                val errorMessage = try {
                    val jsonElement = json.parseToJsonElement(errorBody ?: "")
                    jsonElement.jsonObject["detail"]?.jsonPrimitive?.contentOrNull
                        ?: jsonElement.jsonObject["message"]?.jsonPrimitive?.contentOrNull
                        ?: "HTTP $responseCode"
                } catch (e: Exception) {
                    "HTTP $responseCode"
                }
                NarsLogger.e(TAG, "Login failed: $errorMessage")
                Result.failure(Exception(errorMessage))
            }
        } catch (e: IllegalArgumentException) {
            NarsLogger.w(TAG, "Login validation error: ${e.message}")
            throw e
        } catch (e: Exception) {
            NarsLogger.e(TAG, "Login error")
            Result.failure(Exception("Login error"))
        }
    }

    /**
     * Get current user
     */
    suspend fun getCurrentUser(cookie: String?): Result<User> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/current_user")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "GET"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")

            cookie?.let { connection.setRequestProperty("Authorization", "Bearer $it") }

            connection.connectTimeout = DEFAULT_TIMEOUT_MS
            connection.readTimeout = DEFAULT_TIMEOUT_MS

            val responseCode = connection.responseCode
            if (responseCode == HttpURLConnection.HTTP_OK) {
                val responseBody = connection.inputStream.bufferedReader().use { it.readText() }
                val user = json.decodeFromString(User.serializer(), responseBody)
                Result.success(user)
            } else {
                Result.failure(Exception("Auth check failed: HTTP $responseCode"))
            }
        } catch (e: Exception) {
            Result.failure(Exception("Auth check error: ${e.message}"))
        }
    }

    /**
     * Logout
     */
    suspend fun logout(cookie: String?): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/logout")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "application/json")

            authToken?.let { connection.setRequestProperty("Authorization", "Bearer $it") }
            cookie?.let { connection.setRequestProperty("Cookie", it) }

            connection.connectTimeout = DEFAULT_TIMEOUT_MS
            connection.readTimeout = DEFAULT_TIMEOUT_MS

            val responseCode = connection.responseCode
            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_NO_CONTENT) {
                Result.success(Unit)
            } else {
                Result.failure(Exception("Logout failed: HTTP $responseCode"))
            }
        } catch (e: Exception) {
            Result.failure(Exception("Logout error: ${e.message}"))
        }
    }

    /**
     * Check if user is authenticated
     */
    suspend fun isAuthenticated(cookie: String?): Boolean {
        return getCurrentUser(cookie).isSuccess
    }
}
