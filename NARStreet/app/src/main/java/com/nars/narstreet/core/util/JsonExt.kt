package com.nars.narstreet.core.util

/**
 * Safe numeric extractors for values coming out of Map<String, Any?> after
 * Moshi deserialization via AnyAdapter.
 *
 * AnyAdapter returns:
 *   - Long   for whole-number JSON values  (e.g. 3, 36, 1234567)
 *   - Double for decimal JSON values       (e.g. 36.7539, 7.256)
 *
 * Direct casts like (value as? Double) silently return null when the value is
 * actually a Long (common for integer coordinates like 36.0 stored as 36).
 * These extensions handle both cases correctly.
 */

/** Extracts a Double from Any? regardless of whether it was parsed as Long or Double. */
fun Any?.toAnyDouble(default: Double = 0.0): Double = when (this) {
    is Double -> this
    is Long   -> this.toDouble()
    is Int    -> this.toDouble()
    is Float  -> this.toDouble()
    is Number -> this.toDouble()
    else      -> default
}

/** Extracts a Long from Any? regardless of whether it was parsed as Long or Double. */
fun Any?.toAnyLong(default: Long = 0L): Long = when (this) {
    is Long   -> this
    is Double -> this.toLong()
    is Int    -> this.toLong()
    is Number -> this.toLong()
    else      -> default
}

/** Extracts an Int from Any?. */
fun Any?.toAnyInt(default: Int = 0): Int = when (this) {
    is Int    -> this
    is Long   -> this.toInt()
    is Double -> this.toInt()
    is Number -> this.toInt()
    else      -> default
}
