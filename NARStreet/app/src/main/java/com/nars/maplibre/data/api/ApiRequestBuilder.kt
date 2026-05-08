package com.nars.maplibre.data.api

import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.utils.NarsLogger
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

/**
 * Handles API request building and JSON parsing
 */
class ApiRequestBuilder(private val json: Json) {
    companion object {
        private const val TAG = "ApiRequestBuilder"
    }

    /**
     * Parse backend features response into NarsFeature objects
     */
    fun parseFeaturesFromResponse(responseBody: String): List<NarsFeature> {
        return try {
            val jsonElement = json.parseToJsonElement(responseBody)
            val jsonArray = if (jsonElement is JsonObject && jsonElement.containsKey("features")) {
                jsonElement["features"]?.jsonArray ?: JsonArray(emptyList())
            } else if (jsonElement is JsonArray) {
                jsonElement
            } else {
                NarsLogger.w(TAG, "Unexpected JSON format: $responseBody")
                return emptyList()
            }

            jsonArray.mapNotNull { element ->
                try {
                    val obj = element.jsonObject
                    val id = obj["id"]?.jsonPrimitive?.content ?: return@mapNotNull null
                    val type = obj["type"]?.jsonPrimitive?.content ?: return@mapNotNull null
                    val layer = obj["layer"]?.jsonPrimitive?.content ?: ""
                    val label = obj["label"]?.jsonPrimitive?.content ?: ""
                    val data = obj["data"]?.jsonObject ?: JsonObject(emptyMap())

                    val geometry = parseGeometryFromData(data, type)
                    val phaseKey = mapBackendTypeToPhase(type)
                    val phaseColor = getPhaseColor(phaseKey)
                    val properties = parseFeatureProperties(data, phaseKey, phaseColor, layer)

                    NarsFeature(
                        id = id,
                        dbId = null,
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

    fun buildSaveRequestBody(feature: NarsFeature): String = com.nars.maplibre.data.api.buildSaveRequestBody(feature)

    fun buildDataJson(feature: NarsFeature): String = com.nars.maplibre.data.api.buildDataJson(feature)
}
