package com.nars.narstreet.core.network

import com.squareup.moshi.JsonAdapter
import com.squareup.moshi.JsonReader
import com.squareup.moshi.JsonWriter
import com.squareup.moshi.Moshi
import java.lang.reflect.Type

/**
 * Moshi adapter for Any? — required because FeatureDto.data is Map<String, Any?>.
 *
 * Number handling strategy:
 *  - Read as Double via nextDouble() (always works for any JSON number)
 *  - If the value is a whole number that fits in a Long, return it as Long
 *    (preserves feature IDs, entrance numbers, roadDbIds as Long)
 *  - Otherwise return as Double (lat/lng coordinates)
 *
 * Callers must use the toAnyDouble() / toAnyLong() extensions from JsonExt.kt
 * rather than direct casts, because the backend may send 36 (integer JSON) or
 * 36.700001 (decimal JSON) for the same logical field depending on the value.
 */
internal class AnyAdapter : JsonAdapter<Any>() {

    override fun fromJson(reader: JsonReader): Any? = when (reader.peek()) {
        JsonReader.Token.NULL         -> reader.nextNull()
        JsonReader.Token.BOOLEAN      -> reader.nextBoolean()
        JsonReader.Token.NUMBER       -> {
            val d = reader.nextDouble()
            // Return as Long for whole numbers that fit in a Long; Double otherwise.
            if (d == Math.floor(d) && !d.isInfinite() && !d.isNaN()
                && d >= Long.MIN_VALUE.toDouble() && d <= Long.MAX_VALUE.toDouble())
                d.toLong()
            else
                d
        }
        JsonReader.Token.STRING       -> reader.nextString()
        JsonReader.Token.BEGIN_ARRAY  -> {
            val list = mutableListOf<Any?>()
            reader.beginArray()
            while (reader.hasNext()) list.add(fromJson(reader))
            reader.endArray()
            list
        }
        JsonReader.Token.BEGIN_OBJECT -> {
            val map = LinkedHashMap<String, Any?>()
            reader.beginObject()
            while (reader.hasNext()) map[reader.nextName()] = fromJson(reader)
            reader.endObject()
            map
        }
        else -> { reader.skipValue(); null }
    }

    override fun toJson(writer: JsonWriter, value: Any?) {
        when (value) {
            null         -> writer.nullValue()
            is Boolean   -> writer.value(value)
            is Long      -> writer.value(value)
            is Int       -> writer.value(value.toLong())
            is Double    -> writer.value(value)
            is Float     -> writer.value(value.toDouble())
            is Number    -> writer.value(value.toDouble())
            is String    -> writer.value(value)
            is List<*>   -> {
                writer.beginArray()
                value.forEach { toJson(writer, it) }
                writer.endArray()
            }
            is Map<*, *> -> {
                writer.beginObject()
                value.forEach { (k, v) -> writer.name(k.toString()); toJson(writer, v) }
                writer.endObject()
            }
            else         -> writer.value(value.toString())
        }
    }

    companion object Factory : JsonAdapter.Factory {
        override fun create(type: Type, annotations: Set<Annotation>, moshi: Moshi): JsonAdapter<*>? =
            if (type == Any::class.java) AnyAdapter() else null
    }
}
