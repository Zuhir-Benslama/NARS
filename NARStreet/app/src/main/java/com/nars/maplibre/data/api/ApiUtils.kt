package com.nars.maplibre.data.api

import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.utils.NarsLogger
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.addJsonObject
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonArray
private const val TAG = "ApiUtils"

fun getPhaseColor(phaseKey: String): String = when (phaseKey) {
    "roads" -> "#3498db"
    "houseEntrances" -> "#27ae60"
    "namingPanels" -> "#9b59b6"
    else -> "#8e44ad"
}

fun mapBackendTypeToPhase(type: String): String = when (type) {
    "road" -> Phases.ROADS_KEY
    "house_entrance" -> Phases.HOUSE_ENTRANCES_KEY
    "naming_panel" -> Phases.NAMING_PANELS_KEY
    else -> Phases.ROADS_KEY
}

fun mapPhaseToBackendType(phase: String): String = when (phase) {
    Phases.ROADS_KEY -> "road"
    Phases.HOUSE_ENTRANCES_KEY -> "house_entrance"
    Phases.NAMING_PANELS_KEY -> "naming_panel"
    else -> "road"
}

fun mapPhaseToLayer(phase: String, properties: com.nars.maplibre.data.model.FeatureProperties): String = when (phase) {
    Phases.ROADS_KEY -> properties.roadTypeKey ?: "street"
    Phases.HOUSE_ENTRANCES_KEY -> properties.entranceTypeKey ?: "main_entrance"
    Phases.NAMING_PANELS_KEY -> "naming_panel"
    else -> ""
}

fun parseGeometryFromData(data: JsonObject, type: String): com.nars.maplibre.data.model.Geometry {
    return try {
        val lat = data["lat"]?.jsonPrimitive?.doubleOrNull
        val lng = data["lng"]?.jsonPrimitive?.doubleOrNull
        val radius = data["radius"]?.jsonPrimitive?.doubleOrNull ?: 0.0

        if (lat != null && lng != null) {
            if (radius > 0) {
                com.nars.maplibre.data.model.CircleGeometry(coordinates = listOf(lng, lat, radius))
            } else {
                com.nars.maplibre.data.model.PointGeometry(coordinates = listOf(lng, lat))
            }
        } else {
            val coordsArray = data["coordinates"]?.jsonArray
            if (coordsArray != null) {
                val flatCoords = mutableListOf<Double>()
                for (coord in coordsArray) {
                    val coordObj = coord.jsonObject
                    val cLat = coordObj["lat"]?.jsonPrimitive?.doubleOrNull ?: 0.0
                    val cLng = coordObj["lng"]?.jsonPrimitive?.doubleOrNull ?: 0.0
                    flatCoords.add(cLng)
                    flatCoords.add(cLat)
                }

                if (type == "road") {
                    com.nars.maplibre.data.model.LineStringGeometry(coordinates = flatCoords)
                } else {
                    com.nars.maplibre.data.model.PolygonGeometry(coordinates = flatCoords)
                }
            } else {
                com.nars.maplibre.data.model.PointGeometry(coordinates = listOf(0.0, 0.0))
            }
        }
    } catch (e: Exception) {
        com.nars.maplibre.data.model.PointGeometry(coordinates = listOf(0.0, 0.0))
    }
}

fun parseFeatureProperties(data: JsonObject, phaseKey: String, color: String, layer: String): com.nars.maplibre.data.model.FeatureProperties {
    val labelValue = data["label"]?.jsonPrimitive?.content ?: ""
    val nameValue = labelValue.ifBlank { layer }
    return com.nars.maplibre.data.model.FeatureProperties(
        name = nameValue,
        number = null,
        bisNumber = null,
        phase = phaseKey,
        color = color,
        decisionNumber = data["decisionNumber"]?.jsonPrimitive?.content,
        decisionDate = data["decisionDate"]?.jsonPrimitive?.content,
        roadTypeKey = data["roadTypeKey"]?.jsonPrimitive?.content,
        entranceTypeKey = data["entranceTypeKey"]?.jsonPrimitive?.content,
        roadDbId = data["roadDbId"]?.jsonPrimitive?.content,
        side = data["side"]?.jsonPrimitive?.content,
        entranceNumber = data["entranceNumber"]?.jsonPrimitive?.intOrNull
    )
}

fun escapeJson(s: String): String = s
    .replace("\\", "\\\\")
    .replace("\"", "\\\"")
    .replace("\n", "\\n")
    .replace("\r", "\\r")
    .replace("\t", "\\t")

fun buildDataJson(feature: NarsFeature): String {
    val props = feature.properties
    return buildJsonObject {
        when (val geometry = feature.geometry) {
            is com.nars.maplibre.data.model.PointGeometry -> {
                put("lat", geometry.coordinates[1])
                put("lng", geometry.coordinates[0])
            }
            is com.nars.maplibre.data.model.CircleGeometry -> {
                put("lat", geometry.coordinates[1])
                put("lng", geometry.coordinates[0])
                put("radius", geometry.coordinates[2])
            }
            is com.nars.maplibre.data.model.LineStringGeometry -> {
                putJsonArray("coordinates") {
                    for (coord in geometry.coordinates.chunked(2)) {
                        addJsonObject {
                            put("lat", coord[1])
                            put("lng", coord[0])
                        }
                    }
                }
            }
            is com.nars.maplibre.data.model.PolygonGeometry -> {
                putJsonArray("coordinates") {
                    for (coord in geometry.coordinates.chunked(2)) {
                        addJsonObject {
                            put("lat", coord[1])
                            put("lng", coord[0])
                        }
                    }
                }
            }
        }

        props.name?.takeIf { it.isNotBlank() }?.let { put("label", it) }
        props.decisionNumber?.takeIf { it.isNotBlank() }?.let { put("decisionNumber", it) }
        props.decisionDate?.takeIf { it.isNotBlank() }?.let { put("decisionDate", it) }
        props.roadTypeKey?.takeIf { it.isNotBlank() }?.let { put("roadTypeKey", it) }
        props.entranceTypeKey?.takeIf { it.isNotBlank() }?.let { put("entranceTypeKey", it) }
        props.roadDbId?.let { put("roadDbId", it) }
        props.roadLabel?.takeIf { it.isNotBlank() }?.let { put("roadLabel", it) }
        props.side?.takeIf { it.isNotBlank() }?.let { put("side", it) }
        props.entranceNumber?.let { put("entranceNumber", it) }
        props.mainEntranceDbId?.let { put("mainEntranceDbId", it) }
        props.mainEntranceLabel?.takeIf { it.isNotBlank() }?.let { put("mainEntranceLabel", it) }
        props.bisNumber?.let { put("bisNumber", it) }
    }.toString()
}

fun buildSaveRequestBody(feature: NarsFeature): String {
    val backendType = mapPhaseToBackendType(feature.properties.phase)
    val layer = mapPhaseToLayer(feature.properties.phase, feature.properties)
    val label = feature.properties.name?.takeIf { it.isNotBlank() } ?: "Unnamed"
    val dataJson = buildDataJson(feature)
    return """{"type":"$backendType","layer":"$layer","label":"$label","data":$dataJson}"""
}

fun parseFeaturesFromResponse(json: kotlinx.serialization.json.Json, responseBody: String): List<NarsFeature> {
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
