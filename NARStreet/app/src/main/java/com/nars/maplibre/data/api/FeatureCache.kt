package com.nars.maplibre.data.api

class FeatureCache(private val maxEntries: Int = 50) {
    private val cache = LinkedHashMap<String, CacheEntry>(maxEntries, 0.75f, true)

    data class CacheEntry(
        val data: Any,
        val timestamp: Long = System.currentTimeMillis()
    )

    @Synchronized
    fun get(key: String): Any? = cache[key]?.data

    @Synchronized
    fun put(key: String, data: Any) {
        if (cache.size >= maxEntries) {
            cache.remove(cache.keys.first())
        }
        cache[key] = CacheEntry(data)
    }

    @Synchronized
    fun clear() = cache.clear()

    @Synchronized
    fun remove(key: String) = cache.remove(key)
}
