package com.nars.narstreet.data.remote.dto

import com.squareup.moshi.JsonReader
import okio.Buffer

/**
 * Parses a raw JSON object string into Map<String, Any?>.
 * Called by RawJson.parse() in repositories when extracting fields.
 *
 * Mirrors the same number strategy as RawJsonAdapter.readValue():
 *   whole numbers  → Long   (e.g. 36, 1234567)
 *   decimal numbers → Double (e.g. 36.7539, 3.05)
 */
internal object RawJsonParser {

    fun parseObject(json: String): Map<String, Any?> {
        if (json.isBlank()) return emptyMap()
        return try {
            val reader = JsonReader.of(Buffer().writeUtf8(json))
            reader.isLenient = true
            val result = readObject(reader)
            reader.close()
            android.util.Log.d("NARStreet/RawJson", "parseObject keys=${result.keys}, coords=${result["coordinates"]?.let { (it as? List<*>)?.size } ?: "none"}")
            result
        } catch (_: Exception) {
            emptyMap()
        }
    }

    private fun readValue(reader: JsonReader): Any? = when (reader.peek()) {
        JsonReader.Token.NULL         -> reader.nextNull()
        JsonReader.Token.BOOLEAN      -> reader.nextBoolean()
        JsonReader.Token.NUMBER       -> {
            val d = reader.nextDouble()
            if (d % 1.0 == 0.0 && !d.isInfinite() && !d.isNaN()
                && d in Long.MIN_VALUE.toDouble()..Long.MAX_VALUE.toDouble())
                d.toLong() else d
        }
        JsonReader.Token.STRING       -> reader.nextString()
        JsonReader.Token.BEGIN_ARRAY  -> {
            val list = mutableListOf<Any?>()
            reader.beginArray()
            while (reader.hasNext()) list.add(readValue(reader))
            reader.endArray()
            list
        }
        JsonReader.Token.BEGIN_OBJECT -> readObject(reader)
        else                          -> { reader.skipValue(); null }
    }

    private fun readObject(reader: JsonReader): Map<String, Any?> {
        val map = LinkedHashMap<String, Any?>()
        reader.beginObject()
        while (reader.hasNext()) map[reader.nextName()] = readValue(reader)
        reader.endObject()
        return map
    }
}
