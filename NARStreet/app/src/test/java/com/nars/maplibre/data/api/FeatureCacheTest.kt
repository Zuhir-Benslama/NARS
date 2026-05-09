package com.nars.maplibre.data.api

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Before
import org.junit.Test

class FeatureCacheTest {

    private lateinit var cache: FeatureCache

    @Before
    fun setUp() {
        cache = FeatureCache(maxEntries = 3)
    }

    @Test
    fun `put and get round trip`() {
        cache.put("key1", "value1")
        assertEquals("value1", cache.get("key1"))
    }

    @Test
    fun `get returns null for missing key`() {
        assertNull(cache.get("nonexistent"))
    }

    @Test
    fun `remove eliminates entry`() {
        cache.put("key1", "value1")
        cache.remove("key1")
        assertNull(cache.get("key1"))
    }

    @Test
    fun `clear removes all entries`() {
        cache.put("key1", "value1")
        cache.put("key2", "value2")
        cache.clear()
        assertNull(cache.get("key1"))
        assertNull(cache.get("key2"))
    }

    @Test
    fun `cache evicts oldest when full`() {
        cache.put("key1", "value1")
        cache.put("key2", "value2")
        cache.put("key3", "value3")
        cache.put("key4", "value4")
        assertNull(cache.get("key1"))
        assertEquals("value2", cache.get("key2"))
        assertEquals("value3", cache.get("key3"))
        assertEquals("value4", cache.get("key4"))
    }
}
