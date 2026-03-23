package com.nars.narstreet.data.remote.dto

import com.squareup.moshi.JsonAdapter
import com.squareup.moshi.JsonReader
import com.squareup.moshi.JsonWriter
import com.squareup.moshi.Moshi
import java.lang.reflect.Type

/**
 * Wraps a raw JSON string captured verbatim from the wire.
 *
 * Used for FeatureDto.data because Map<String, Any?> is not reliably
 * handled by Moshi KSP — the generated adapter cannot resolve Any?.
 *
 * Call [parse] to get a Map<String, Any?> for field extraction.
 */
data class RawJson(val json: String) {

    /** Parses the captured JSON object into Map<String, Any?>. Never throws. */
    fun parse(): Map<String, Any?> = RawJsonParser.parseObject(json)

    override fun toString(): String = json

    companion object {
        /**
         * Moshi JsonAdapter.Factory for RawJson.
         * Reads the entire JSON value token-by-token and stores it as a string.
         * Uses Android's built-in org.json for serialisation — no external deps.
         */
        val ADAPTER_FACTORY = JsonAdapter.Factory { type: Type, _: Set<Annotation>, _: Moshi ->
            if (type == RawJson::class.java) RawJsonAdapter() else null
        }
    }
}

/**
 * Reads a JSON value from the stream and stores it verbatim as a string,
 * then re-serialises it for toJson. Uses only Moshi's JsonReader/Writer
 * primitives — no peekJson(), no jsonValue(), no Buffer trickery.
 */
private class RawJsonAdapter : JsonAdapter<RawJson>() {

    override fun fromJson(reader: JsonReader): RawJson {
        val value = readValue(reader)
        return RawJson(valueToJsonString(value))
    }

    override fun toJson(writer: JsonWriter, value: RawJson?) {
        if (value == null) { writer.nullValue(); return }
        // Parse the stored JSON string back to an object tree and stream it out
        val obj = RawJsonParser.parseObject(value.json)
        writeMap(writer, obj)
    }

    // ── Read any JSON value into a Kotlin type ────────────────────────────────

    private fun readValue(reader: JsonReader): Any? = when (reader.peek()) {
        JsonReader.Token.NULL         -> reader.nextNull()
        JsonReader.Token.BOOLEAN      -> reader.nextBoolean()
        JsonReader.Token.NUMBER       -> {
            // nextDouble() always works for any JSON number
            val d = reader.nextDouble()
            // Return Long for whole numbers, Double for decimals
            if (d % 1.0 == 0.0 && !d.isInfinite() && !d.isNaN()
                && d in Long.MIN_VALUE.toDouble()..Long.MAX_VALUE.toDouble())
                d.toLong() else d
        }
        JsonReader.Token.STRING       -> reader.nextString()
        JsonReader.Token.BEGIN_ARRAY  -> {
            reader.beginArray()
            val list = mutableListOf<Any?>()
            while (reader.hasNext()) list.add(readValue(reader))
            reader.endArray()
            list
        }
        JsonReader.Token.BEGIN_OBJECT -> {
            reader.beginObject()
            val map = LinkedHashMap<String, Any?>()
            while (reader.hasNext()) map[reader.nextName()] = readValue(reader)
            reader.endObject()
            map
        }
        else -> { reader.skipValue(); null }
    }

    // ── Serialise back to a JSON string ───────────────────────────────────────

    private fun valueToJsonString(value: Any?): String = when (value) {
        null         -> "null"
        is Boolean   -> value.toString()
        is Long      -> value.toString()
        is Double    -> value.toBigDecimal().stripTrailingZeros().toPlainString()
        is Number    -> value.toString()
        is String    -> buildString {
            append('"')
            value.forEach { c ->
                when (c) {
                    '"'  -> append("\\\"")
                    '\\' -> append("\\\\")
                    '\n' -> append("\\n")
                    '\r' -> append("\\r")
                    '\t' -> append("\\t")
                    else -> append(c)
                }
            }
            append('"')
        }
        is List<*>   -> "[" + value.joinToString(",") { valueToJsonString(it) } + "]"
        is Map<*, *> -> "{" + value.entries.joinToString(",") { (k, v) ->
            valueToJsonString(k.toString()) + ":" + valueToJsonString(v)
        } + "}"
        else         -> valueToJsonString(value.toString())
    }

    // ── Stream a Map to a JsonWriter (for toJson) ─────────────────────────────

    private fun writeMap(writer: JsonWriter, map: Map<String, Any?>) {
        writer.beginObject()
        map.forEach { (k, v) -> writer.name(k); writeValue(writer, v) }
        writer.endObject()
    }

    private fun writeValue(writer: JsonWriter, value: Any?) {
        when (value) {
            null         -> writer.nullValue()
            is Boolean   -> writer.value(value)
            is Long      -> writer.value(value)
            is Double    -> writer.value(value)
            is Number    -> writer.value(value.toDouble())
            is String    -> writer.value(value)
            is List<*>   -> {
                writer.beginArray()
                value.forEach { writeValue(writer, it) }
                writer.endArray()
            }
            is Map<*, *> -> {
                @Suppress("UNCHECKED_CAST")
                writeMap(writer, value as Map<String, Any?>)
            }
            else         -> writer.value(value.toString())
        }
    }
}
