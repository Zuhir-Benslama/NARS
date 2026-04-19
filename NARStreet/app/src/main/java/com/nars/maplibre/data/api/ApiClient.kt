package com.nars.maplibre.data.api

import com.nars.maplibre.BuildConfig
import com.nars.maplibre.data.model.LoginRequest
import com.nars.maplibre.data.model.LoginResponse
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.User
import com.nars.maplibre.utils.Config
import com.nars.maplibre.utils.NarsLogger
import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import java.net.HttpURLConnection
import java.net.URL

private const val TAG = "ApiClient"

// Extension for boolean parsing from JsonPrimitive
val JsonPrimitive.booleanOrNull: Boolean?
    get() = contentOrNull?.toBoolean()

/**
 * API Client for NARS backend
 * Handles authentication and data synchronization
 */
class ApiClient(
    private val baseUrl: String = BuildConfig.API_BASE_URL
) {
    private val json = Json {
        ignoreUnknownKeys = true
        isLenient = true
    }

    private var authToken: String? = null
    private var cookie: String? = null

    /**
     * Validate URL format
     */
    private fun isValidUrl(url: String): Boolean {
        return url.startsWith("http://") || url.startsWith("https://")
    }

    /**
     * Validate input string is not blank
     */
    private fun isNotBlank(value: String?): Boolean {
        return !value.isNullOrBlank()
    }

    /**
     * Set authentication token
     */
    fun setAuthToken(token: String?) {
        authToken = token
    }

    /**
     * Get authentication token
     */
    fun getAuthToken(): String? {
        return authToken
    }

    /**
     * Set session cookie
     */
    fun setCookie(cookie: String?) {
        this.cookie = cookie
    }

    /**
     * Get session cookie
     */
    fun getCookie(): String? {
        return cookie
    }

    /**
     * Login with username and password
     */
    suspend fun login(username: String, password: String): Result<LoginResponse> = withContext(Dispatchers.IO) {
        // Input validation
        require(isNotBlank(username)) { "Username cannot be blank" }
        require(isNotBlank(password)) { "Password cannot be blank" }
        require(username.length <= 100) { "Username too long (max 100 characters)" }
        require(password.length <= 200) { "Password too long (max 200 characters)" }
        require(isValidUrl(baseUrl)) { "Invalid API base URL configuration" }

        NarsLogger.logAuthEvent(TAG, "Login attempt", username)

        try {
            val url = URL("$baseUrl/api/signin")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")
            connection.doOutput = true
            connection.connectTimeout = 15000
            connection.readTimeout = 15000
            connection.instanceFollowRedirects = false

            val requestBody = json.encodeToString(LoginRequest.serializer(), LoginRequest(username, password))
            connection.outputStream.use { os ->
                os.write(requestBody.toByteArray())
            }

            val responseCode = connection.responseCode

            // Get cookies from response and extract access_token
            val cookies = connection.headerFields["Set-Cookie"]
            cookies?.firstOrNull()?.let { cookieValue ->
                // Extract access_token value from cookie string
                // Format: access_token=eyJhbG...; max-age=86400; path=/; secure; samesite=lax; httponly
                val tokenMatch = Regex("access_token=([^;]+)").find(cookieValue)
                tokenMatch?.let { match ->
                    cookie = match.groupValues[1] // Store the JWT token
                }
            }

            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_CREATED) {
                val responseBody = connection.inputStream.bufferedReader().use { it.readText() }

                // Parse response manually to extract commune coordinates
                return@withContext try {
                    val jsonElement = json.parseToJsonElement(responseBody)
                    
                    // Check for success flag (ASP.NET Core Identity response)
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

                    // Extract commune coordinates
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

                    // Backend uses cookie-based auth, but also check for token
                    val token = jsonElement.jsonObject["token"]?.jsonPrimitive?.contentOrNull
                        ?: jsonElement.jsonObject["accessToken"]?.jsonPrimitive?.contentOrNull
                    val municipalityName = jsonElement.jsonObject["municipalityName"]?.jsonPrimitive?.contentOrNull
                        ?: jsonElement.jsonObject["commune"]?.jsonObject?.get("name_fr")?.jsonPrimitive?.contentOrNull

                    // Store token if present, otherwise use cookie-based auth
                    if (token != null) {
                        authToken = token
                    }
                    
                    NarsLogger.logAuthEvent(TAG, "Login successful", username)
                    Result.success(LoginResponse(user, token, municipalityName))
                } catch (e: Exception) {
                    // If parsing fails, create a basic success response
                    NarsLogger.w(TAG, "Login response parse error: ${e.message}")
                    Result.success(LoginResponse(
                        user = User(id = 0, username = username, name = username),
                        token = null,
                        municipalityName = null
                    ))
                }
            } else if (responseCode == 302 || responseCode == 303) {
                // Redirect after successful login - session-based auth
                NarsLogger.logAuthEvent(TAG, "Login redirect - session-based auth", username)
                return@withContext Result.success(LoginResponse(
                    user = User(id = 0, username = username, name = username),
                    token = null,
                    municipalityName = null
                ))
            } else {
                // Try to parse error message from response
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
            // Re-throw validation errors
            NarsLogger.w(TAG, "Login validation error: ${e.message}")
            throw e
        } catch (e: Exception) {
            NarsLogger.e(TAG, "Login error")
            Result.failure(Exception("Login error"))
        }
    }

    /**
     * Load all features for current user
     */
    suspend fun loadFeatures(): Result<List<NarsFeature>> = withContext(Dispatchers.IO) {
        require(isValidUrl(baseUrl)) { "Invalid API base URL configuration" }

        try {
            val url = URL("$baseUrl/api/load")

            // Create connection with auth headers
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "GET"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")

            // Add auth headers - use cookie value as Bearer token
            // Backend returns JWT in access_token cookie, use it as Authorization header
            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
                Log.d(TAG, "Using auth token: ${token.take(30)}...")
            } ?: run {
                Log.w(TAG, "No auth cookie set! Request will likely fail with 401")
            }

            connection.connectTimeout = Config.API_DEFAULT_TIMEOUT_MS
            connection.readTimeout = Config.API_DEFAULT_TIMEOUT_MS

            Log.d(TAG, "Making request to: $url")
            Log.d(TAG, "Request headers: Content-Type=application/json, Accept=application/json")
            cookie?.let { Log.d(TAG, "Authorization header set") }

            val responseCode = connection.responseCode
            Log.d(TAG, "loadFeatures response code: $responseCode")

            if (responseCode == HttpURLConnection.HTTP_OK) {
                val responseBody = connection.inputStream.bufferedReader().use { it.readText() }
                Log.d(TAG, "=== RAW RESPONSE ===")
                Log.d(TAG, "Length: ${responseBody.length}")
                Log.d(TAG, "Body (first 2000): ${responseBody.take(2000)}")
                if (responseBody.length > 2000) {
                    Log.d(TAG, "Body (2000-end): ${responseBody.takeLast(2000)}")
                }
                Log.d(TAG, "===================")

                if (responseBody.isBlank()) {
                    Log.w(TAG, "Response body is empty!")
                    Result.success(emptyList())
                } else {
                    // Debug: Print first 500 chars to see JSON structure
                    Log.d(TAG, "Response first 500: ${responseBody.take(500)}")
                    
                    val features = parseFeaturesFromResponse(responseBody)
                    Log.d(TAG, "Parsed ${features.size} features from response")
                    
                    // Debug: print each parsed feature
                    features.forEachIndexed { idx, f ->
                        Log.d(TAG, "  [$idx] Feature: id=${f.id}, phase=${f.properties.phase}, type=${f.type}, name=${f.properties.name}")
                    }
                    
                    Result.success(features)
                }
            } else if (responseCode == HttpURLConnection.HTTP_UNAUTHORIZED) {
                Log.w(TAG, "Unauthorized - no valid auth token")
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
     * Parse backend features response into NarsFeature objects
     */
    private fun parseFeaturesFromResponse(responseBody: String): List<NarsFeature> {
        return try {
            // Handle wrapper format: { "features": [...], "count": X }
            val jsonElement = Json.parseToJsonElement(responseBody)
            val jsonArray = if (jsonElement is JsonObject && jsonElement.containsKey("features")) {
                jsonElement["features"]?.jsonArray ?: JsonArray(emptyList())
            } else if (jsonElement is JsonArray) {
                jsonElement
            } else {
                Log.w(TAG, "Unexpected JSON format: $responseBody")
                return emptyList()
            }

            jsonArray.mapNotNull { element ->
                try {
                    val obj = element.jsonObject
                    // ID is now a UUID string, not an integer
                    val id = obj["id"]?.jsonPrimitive?.content ?: return@mapNotNull null
                    val type = obj["type"]?.jsonPrimitive?.content ?: return@mapNotNull null
                    val layer = obj["layer"]?.jsonPrimitive?.content ?: ""
                    val label = obj["label"]?.jsonPrimitive?.content ?: ""
                    val data = obj["data"]?.jsonObject ?: JsonObject(emptyMap())

                    // Parse geometry from data field
                    val geometry = parseGeometryFromData(data, type)

                    // Get phase key and corresponding color
                    val phaseKey = mapBackendTypeToPhase(type)
                    val phaseColor = getPhaseColor(phaseKey)

                    // Parse all data fields into FeatureProperties
                    val properties = parseFeatureProperties(data, phaseKey, phaseColor, layer)

                    NarsFeature(
                        id = id,
                        dbId = null, // UUID doesn't have numeric dbId
                        type = com.nars.maplibre.data.model.NarsFeatureType.fromValue(type),
                        geometry = geometry,
                        properties = properties
                    )
                } catch (e: Exception) {
                    null
                }
            }
        } catch (e: Exception) {
            emptyList()
        }
    }

    /**
     * Get phase color by phase key
     */
    private fun getPhaseColor(phaseKey: String): String {
        return when (phaseKey) {
            "areas" -> "#8e44ad"
            "districts" -> "#f39c12"
            "cityCenter" -> "#e74c3c"
            "roads" -> "#3498db"
            "houseEntrances" -> "#27ae60"
            "publicBuildings" -> "#e67e22"
            "publicSpaces" -> "#2ecc71"
            "namingPanels" -> "#9b59b6"
            else -> "#8e44ad"
        }
    }

    /**
     * Parse all feature properties from backend data JSON
     */
    private fun parseFeatureProperties(data: JsonObject, phaseKey: String, color: String, layer: String): com.nars.maplibre.data.model.FeatureProperties {
        val labelValue = data["label"]?.jsonPrimitive?.content ?: ""
        val nameValue = labelValue.ifBlank { layer }
        return com.nars.maplibre.data.model.FeatureProperties(
            name = nameValue,
            number = null,
            bisNumber = null,
            entranceType = null,
            buildingType = null,
            phase = phaseKey,
            color = color,
            decisionNumber = data["decisionNumber"]?.jsonPrimitive?.content,
            decisionDate = data["decisionDate"]?.jsonPrimitive?.content,
            // Area-specific
            areaTypeKey = data["areaTypeKey"]?.jsonPrimitive?.content,
            // District-specific
            districtTypeKey = data["districtTypeKey"]?.jsonPrimitive?.content,
            // Road-specific
            roadTypeKey = data["roadTypeKey"]?.jsonPrimitive?.content,
            // House entrance-specific
            entranceTypeKey = data["entranceTypeKey"]?.jsonPrimitive?.content,
            roadDbId = data["roadDbId"]?.jsonPrimitive?.content?.toLongOrNull(),
            side = data["side"]?.jsonPrimitive?.content,
            entranceNumber = data["entranceNumber"]?.jsonPrimitive?.intOrNull,
            // Public building-specific
            sectorKey = data["sectorKey"]?.jsonPrimitive?.content,
            buildingTypeKey = data["buildingTypeKey"]?.jsonPrimitive?.content,
            // Public space-specific
            spaceTypeKey = data["spaceTypeKey"]?.jsonPrimitive?.content
        )
    }

    /**
     * Parse geometry from backend data JSON
     */
    private fun parseGeometryFromData(data: JsonObject, type: String): com.nars.maplibre.data.model.Geometry {
        return try {
            // Check if it's a point (has lat/lng) or polygon/line (has coordinates array)
            val lat = data["lat"]?.jsonPrimitive?.doubleOrNull
            val lng = data["lng"]?.jsonPrimitive?.doubleOrNull
            val radius = data["radius"]?.jsonPrimitive?.doubleOrNull ?: 0.0
            
            if (lat != null && lng != null) {
                // Point or Circle
                if (radius > 0) {
                    com.nars.maplibre.data.model.CircleGeometry(
                        coordinates = listOf(lng, lat, radius)
                    )
                } else {
                    com.nars.maplibre.data.model.PointGeometry(
                        coordinates = listOf(lng, lat)
                    )
                }
            } else {
                // Polygon or LineString - has coordinates array
                val coordsArray = data["coordinates"]?.jsonArray
                if (coordsArray != null) {
                    val flatCoords = mutableListOf<Double>()
                    for (coord in coordsArray) {
                        val coordObj = coord.jsonObject
                        val cLat = coordObj["lat"]?.jsonPrimitive?.doubleOrNull ?: 0.0
                        val cLng = coordObj["lng"]?.jsonPrimitive?.doubleOrNull ?: 0.0
                        flatCoords.add(cLng) // GeoJSON uses [lng, lat]
                        flatCoords.add(cLat)
                    }
                    
                    // Determine if polygon or line based on type
                    if (type == "road") {
                        com.nars.maplibre.data.model.LineStringGeometry(
                            coordinates = flatCoords
                        )
                    } else {
                        com.nars.maplibre.data.model.PolygonGeometry(
                            coordinates = flatCoords
                        )
                    }
                } else {
                    // Default to point if no geometry found
                    com.nars.maplibre.data.model.PointGeometry(coordinates = listOf(0.0, 0.0))
                }
            }
        } catch (e: Exception) {
            com.nars.maplibre.data.model.PointGeometry(coordinates = listOf(0.0, 0.0))
        }
    }

    /**
     * Map backend feature type to phase key
     */
    private fun mapBackendTypeToPhase(type: String): String {
        return when (type) {
            "area" -> "areas"
            "district" -> "districts"
            "city_center" -> "cityCenter"
            "road" -> "roads"
            "house_entrance" -> "houseEntrances"
            "public_building" -> "publicBuildings"
            "public_space" -> "publicSpaces"
            "naming_panel" -> "namingPanels"
            else -> "areas"
        }
    }

    /**
     * Save a feature
     */
    suspend fun saveFeature(feature: NarsFeature): Result<Long> = withContext(Dispatchers.IO) {
        require(isValidUrl(baseUrl)) { "Invalid API base URL configuration" }

        try {
            // Ensure no trailing slash on baseUrl
            val cleanBaseUrl = baseUrl.trimEnd('/')
            val url = URL("$cleanBaseUrl/api/save")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")
            connection.doOutput = true

            // Add auth headers - use cookie value as Bearer token
            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
                android.util.Log.d(TAG, "Using auth token: ${token.take(20)}...")
            } ?: run {
                android.util.Log.w(TAG, "No auth cookie set!")
            }

            connection.connectTimeout = 15000
            connection.readTimeout = 15000

            // Build request body matching backend expectations
            val requestBody = buildSaveRequestBody(feature)
            android.util.Log.d(TAG, "Save request body: $requestBody")
            android.util.Log.d(TAG, "Save URL: $url")

            connection.outputStream.use { os ->
                os.write(requestBody.toByteArray())
            }

            val responseCode = connection.responseCode
            android.util.Log.d(TAG, "Save response code: $responseCode")

            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_CREATED) {
                val responseBody = connection.inputStream.bufferedReader().use { it.readText() }
                android.util.Log.d(TAG, "Save response body: $responseBody")
                // Parse response to get feature ID
                val id = json.parseToJsonElement(responseBody).jsonObject["id"]?.jsonPrimitive?.longOrNull ?: 0L
                Result.success(id)
            } else {
                val errorBody = try {
                    connection.errorStream?.bufferedReader()?.use { it.readText() } ?: ""
                } catch (e: Exception) { "" }
                android.util.Log.e(TAG, "Save failed: HTTP $responseCode, body: $errorBody")
                Result.failure(Exception("Save feature failed: HTTP $responseCode - $errorBody"))
            }
        } catch (e: Exception) {
            android.util.Log.e(TAG, "Save feature error: ${e.message}", e)
            Result.failure(Exception("Save feature error: ${e.message}"))
        }
    }

    /**
     * Build save request body matching backend DTO
     */
    private fun buildSaveRequestBody(feature: NarsFeature): String {
        val backendType = mapPhaseToBackendType(feature.properties.phase)
        val layer = mapPhaseToLayer(feature.properties.phase, feature.properties)
        val label = feature.properties.name?.takeIf { it.isNotBlank() } ?: "Unnamed"
        val dataJson = buildDataJson(feature)
        return """{"type":"$backendType","layer":"$layer","label":"$label","data":$dataJson}"""
    }

    /**
     * Build full data object including geometry + all modal fields
     */
    private fun buildDataJson(feature: NarsFeature): String {
        val props = feature.properties
        val geometryPart = buildGeometryJson(feature.geometry)

        // Start with geometry fields
        val fields = mutableListOf<String>()

        // Add geometry fields (lat/lng or coordinates)
        when (feature.geometry) {
            is com.nars.maplibre.data.model.PointGeometry -> {
                val lng = feature.geometry.coordinates[0]
                val lat = feature.geometry.coordinates[1]
                fields.add("\"lat\":$lat")
                fields.add("\"lng\":$lng")
            }
            is com.nars.maplibre.data.model.CircleGeometry -> {
                val lng = feature.geometry.coordinates[0]
                val lat = feature.geometry.coordinates[1]
                val radius = feature.geometry.coordinates[2]
                fields.add("\"lat\":$lat")
                fields.add("\"lng\":$lng")
                fields.add("\"radius\":$radius")
            }
            is com.nars.maplibre.data.model.LineStringGeometry -> {
                val coords = feature.geometry.coordinates.chunked(2).map { coord ->
                    """{"lat":${coord[1]},"lng":${coord[0]}}"""
                }.joinToString(",")
                fields.add("\"coordinates\":[$coords]")
            }
            is com.nars.maplibre.data.model.PolygonGeometry -> {
                val coords = feature.geometry.coordinates.chunked(2).map { coord ->
                    """{"lat":${coord[1]},"lng":${coord[0]}}"""
                }.joinToString(",")
                fields.add("\"coordinates\":[$coords]")
            }
        }

        // Add common fields
        props.name?.takeIf { it.isNotBlank() }?.let { fields.add("\"label\":\"${escapeJson(it)}\"") }
        props.decisionNumber?.takeIf { it.isNotBlank() }?.let { fields.add("\"decisionNumber\":\"${escapeJson(it)}\"") }
        props.decisionDate?.takeIf { it.isNotBlank() }?.let { fields.add("\"decisionDate\":\"${escapeJson(it)}\"") }

        // Add phase-specific type keys
        props.areaTypeKey?.takeIf { it.isNotBlank() }?.let { fields.add("\"areaTypeKey\":\"$it\"") }
        props.districtTypeKey?.takeIf { it.isNotBlank() }?.let { fields.add("\"districtTypeKey\":\"$it\"") }
        props.roadTypeKey?.takeIf { it.isNotBlank() }?.let { fields.add("\"roadTypeKey\":\"$it\"") }
        props.entranceTypeKey?.takeIf { it.isNotBlank() }?.let { fields.add("\"entranceTypeKey\":\"$it\"") }
        props.spaceTypeKey?.takeIf { it.isNotBlank() }?.let { fields.add("\"spaceTypeKey\":\"$it\"") }
        props.sectorKey?.takeIf { it.isNotBlank() }?.let { fields.add("\"sectorKey\":\"$it\"") }
        props.buildingTypeKey?.takeIf { it.isNotBlank() }?.let { fields.add("\"buildingTypeKey\":\"$it\"") }

        // Add house entrance specific fields
        props.roadDbId?.let { fields.add("\"roadDbId\":$it") }
        props.roadLabel?.takeIf { it.isNotBlank() }?.let { fields.add("\"roadLabel\":\"${escapeJson(it)}\"") }
        props.side?.takeIf { it.isNotBlank() }?.let { fields.add("\"side\":\"$it\"") }
        props.entranceNumber?.let { fields.add("\"entranceNumber\":$it") }
        props.mainEntranceDbId?.let { fields.add("\"mainEntranceDbId\":$it") }
        props.mainEntranceLabel?.takeIf { it.isNotBlank() }?.let { fields.add("\"mainEntranceLabel\":\"${escapeJson(it)}\"") }
        props.bisNumber?.let { fields.add("\"bisNumber\":$it") }

        // Add radius for city center
        // Note: radius would need to be stored in properties if available

        return "{${fields.joinToString(",")}}"
    }

    private fun escapeJson(s: String): String {
        return s.replace("\\", "\\\\").replace("\"", "\\\"").replace("\n", "\\n").replace("\r", "\\r").replace("\t", "\\t")
    }

    /**
     * Map phase key + properties to backend layer value
     */
    private fun mapPhaseToLayer(phase: String, properties: com.nars.maplibre.data.model.FeatureProperties): String {
        return when (phase) {
            "areas" -> properties.areaTypeKey ?: "central_urban"
            "districts" -> properties.districtTypeKey ?: "district"
            "cityCenter" -> "city_center"
            "roads" -> properties.roadTypeKey ?: "street"
            "houseEntrances" -> properties.entranceTypeKey ?: "main_entrance"
            "publicBuildings" -> properties.buildingTypeKey ?: "public_building"
            "publicSpaces" -> properties.spaceTypeKey ?: "garden"
            "namingPanels" -> "naming_panel"
            else -> ""
        }
    }

    /**
     * Build geometry JSON from NARS geometry
     */
    private fun buildGeometryJson(geometry: com.nars.maplibre.data.model.Geometry): String {
        return when (geometry) {
            is com.nars.maplibre.data.model.PointGeometry -> {
                val lng = geometry.coordinates[0]
                val lat = geometry.coordinates[1]
                """{"lat":$lat,"lng":$lng}"""
            }
            is com.nars.maplibre.data.model.CircleGeometry -> {
                val lng = geometry.coordinates[0]
                val lat = geometry.coordinates[1]
                val radius = geometry.coordinates[2]
                """{"lat":$lat,"lng":$lng,"radius":$radius}"""
            }
            is com.nars.maplibre.data.model.LineStringGeometry -> {
                val coords = geometry.coordinates.chunked(2).map { coord ->
                    """{"lat":${coord[1]},"lng":${coord[0]}}"""
                }.joinToString(",")
                """{"coordinates":[$coords]}"""
            }
            is com.nars.maplibre.data.model.PolygonGeometry -> {
                val coords = geometry.coordinates.chunked(2).map { coord ->
                    """{"lat":${coord[1]},"lng":${coord[0]}}"""
                }.joinToString(",")
                """{"coordinates":[$coords]}"""
            }
        }
    }

    /**
     * Map phase key to backend feature type
     */
    private fun mapPhaseToBackendType(phase: String): String {
        return when (phase) {
            "areas" -> "area"
            "districts" -> "district"
            "cityCenter" -> "city_center"
            "roads" -> "road"
            "houseEntrances" -> "house_entrance"
            "publicBuildings" -> "public_building"
            "publicSpaces" -> "public_space"
            "namingPanels" -> "naming_panel"
            else -> "area"
        }
    }

    /**
     * Delete a feature
     */
    suspend fun deleteFeature(featureId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/delete/$featureId")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "DELETE"
            connection.setRequestProperty("Accept", "application/json")

            // Add auth headers - use cookie value as Bearer token
            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
            }

            connection.connectTimeout = 10000
            connection.readTimeout = 10000

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

    /**
     * Update a feature
     */
    suspend fun updateFeature(featureId: String, feature: NarsFeature): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/update/$featureId")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "PUT"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")
            connection.doOutput = true

            // Add auth headers - use cookie value as Bearer token
            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
            }

            connection.connectTimeout = 15000
            connection.readTimeout = 15000

            // Build request body matching backend expectations
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
     * Get current user
     */
    suspend fun getCurrentUser(): Result<User> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/current_user")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "GET"
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")

            // Add auth headers - use cookie value as Bearer token
            cookie?.let { token ->
                connection.setRequestProperty("Authorization", "Bearer $token")
            }

            connection.connectTimeout = 10000
            connection.readTimeout = 10000

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
    suspend fun logout(): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val url = URL("$baseUrl/api/logout")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "application/json")

            authToken?.let { connection.setRequestProperty("Authorization", "Bearer $it") }
            cookie?.let { connection.setRequestProperty("Cookie", it) }

            connection.connectTimeout = 10000
            connection.readTimeout = 10000

            val responseCode = connection.responseCode
            if (responseCode == HttpURLConnection.HTTP_OK || responseCode == HttpURLConnection.HTTP_NO_CONTENT) {
                authToken = null
                cookie = null
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
    suspend fun isAuthenticated(): Boolean {
        return getCurrentUser().isSuccess
    }
}
