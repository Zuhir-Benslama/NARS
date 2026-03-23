package com.nars.narstreet.data.model

import androidx.room.Entity
import androidx.room.PrimaryKey
import com.nars.narstreet.data.remote.dto.FeatureUpdateDto

@Entity(tableName = "areas")
data class AreaEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val remoteId: Long,
    val label: String,
    val layer: String,             // central_urban | secondary_urban
    val coordinatesJson: String = "[]",
    val dataJson: String        = "{}",
    val syncStatus: SyncStatus  = SyncStatus.SYNCED,
) {
    fun toUpdateDto(): FeatureUpdateDto = FeatureUpdateDto(
        label = label,
        // Deserialize coordinatesJson back to a List<Map> so the server receives
        // a proper JSON array, not a string-wrapped array.
        data  = mapOf("coordinates" to coordinatesJsonToList(coordinatesJson)),
    )
}

/**
 * Parses a JSON string like `[{"lat":36.7,"lng":3.05},...]` into
 * `List<Map<String,Double>>` so Moshi serialises it as a proper JSON array.
 * Uses JsonReader directly — no regex, handles spaces and any number format.
 */
internal fun coordinatesJsonToList(json: String): List<Map<String, Double>> {
    if (json.isBlank() || json == "[]") return emptyList()
    return try {
        val reader = com.squareup.moshi.JsonReader.of(okio.Buffer().writeUtf8(json))
        reader.isLenient = true
        val result = mutableListOf<Map<String, Double>>()
        reader.beginArray()
        while (reader.hasNext()) {
            var lat = 0.0; var lng = 0.0
            reader.beginObject()
            while (reader.hasNext()) {
                when (reader.nextName()) {
                    "lat" -> lat = reader.nextDouble()
                    "lng" -> lng = reader.nextDouble()
                    else  -> reader.skipValue()
                }
            }
            reader.endObject()
            result.add(mapOf("lat" to lat, "lng" to lng))
        }
        reader.endArray()
        result
    } catch (_: Exception) { emptyList() }
}
