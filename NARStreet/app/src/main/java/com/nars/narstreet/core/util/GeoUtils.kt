package com.nars.narstreet.core.util

/**
 * Shared coordinate extractor used by all feature repositories.
 *
 * Moshi deserialises Map<String,Any?> so coordinates arrive as List<Map<*,*>>
 * with Double/Int/Long values. This serialises them to standard JSON so that
 * parseCoordinatesJson (MapView.kt) can decode them reliably.
 */
@Suppress("UNCHECKED_CAST")
fun extractCoords(data: Map<String, Any?>): String {
    return try {
        val coords = data["coordinates"] as? List<*>
            ?: return "[]"
        if (coords.isEmpty()) return "[]"

        "[" + coords.joinToString(",") { item ->
            val c = item as? Map<*, *> ?: return@joinToString "{}"
            val lat = when (val v = c["lat"]) {
                is Double -> v
                is Int    -> v.toDouble()
                is Long   -> v.toDouble()
                is Float  -> v.toDouble()
                is Number -> v.toDouble()
                else      -> 0.0
            }
            val lng = when (val v = c["lng"]) {
                is Double -> v
                is Int    -> v.toDouble()
                is Long   -> v.toDouble()
                is Float  -> v.toDouble()
                is Number -> v.toDouble()
                else      -> 0.0
            }
            """{"lat":$lat,"lng":$lng}"""
        } + "]"
    } catch (_: Exception) { "[]" }
}
