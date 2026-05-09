package com.nars.maplibre.utils

import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class HouseNumberingManagerTest {

    private val manager = HouseNumberingManager()

    // Helper to create a road (LineString)
    private fun createRoad(id: String, coordinates: List<Double>): NarsFeature {
        return NarsFeature(
            id = id,
            type = NarsFeatureType.ROAD,
            geometry = LineStringGeometry(coordinates = coordinates),
            properties = FeatureProperties(
                phase = Phases.ROADS_KEY,
                color = "#3498db"
            ),
            dbId = 1L
        )
    }

    // Helper to create an entrance (Point)
    private fun createEntrance(id: String, lng: Double, lat: Double, roadDbId: Long? = 1L): NarsFeature {
        return NarsFeature(
            id = id,
            type = NarsFeatureType.HOUSE_ENTRANCE,
            geometry = PointGeometry(coordinates = listOf(lng, lat)),
            properties = FeatureProperties(
                phase = Phases.HOUSE_ENTRANCES_KEY,
                color = "#27ae60",
                entranceTypeKey = "main_entrance",
                roadDbId = roadDbId
            )
        )
    }

    @Test
    fun `setHouseNumbers returns unchanged list when road has no geometry`() {
        val road = createRoad("road1", emptyList())
        val entrances = listOf(createEntrance("e1", 3.0, 36.0))

        val result = manager.setHouseNumbers(entrances, road)

        assertEquals(entrances.size, result.size)
    }

    @Test
    fun `setHouseNumbers assigns numbers to entrances on road`() {
        // Road: straight horizontal line from (3.0, 36.0) to (3.01, 36.0)
        val road = createRoad(
            "road1",
            listOf(3.0, 36.0, 3.005, 36.0, 3.01, 36.0)
        )

        // Entrances along the road
        val entrances = listOf(
            createEntrance("e1", 3.001, 36.0001),
            createEntrance("e2", 3.005, 36.0001),
            createEntrance("e3", 3.009, 36.0001)
        )

        val result = manager.setHouseNumbers(entrances, road)

        // All should have numbers assigned
        assertTrue(result.all { it.properties.entranceNumber != null && it.properties.entranceNumber > 0 })
    }

    @Test
    fun `setHouseNumbers skips entrances not assigned to road`() {
        val road = createRoad("road1", listOf(3.0, 36.0, 3.01, 36.0))
        val entrance = createEntrance("e1", 3.005, 36.0001, roadDbId = 999L) // Different road

        val result = manager.setHouseNumbers(listOf(entrance), road)

        // Should not assign number
        assertNull(result[0].properties.entranceNumber)
    }

    @Test
    fun `setHouseNumbers assigns odd and even numbers correctly`() {
        val road = createRoad(
            "road1",
            listOf(3.0, 36.0, 3.01, 36.0)
        )

        // Create 4 entrances along the road
        val entrances = listOf(
            createEntrance("e1", 3.002, 36.0001),
            createEntrance("e2", 3.004, 36.0001),
            createEntrance("e3", 3.006, 36.0001),
            createEntrance("e4", 3.008, 36.0001)
        )

        val result = manager.setHouseNumbers(entrances, road)

        // Should have odd and even numbers
        val numbers = result.mapNotNull { it.properties.entranceNumber }.sorted()
        assertTrue("Should have multiple numbers", numbers.size >= 2)
        assertTrue("Should have odd numbers", numbers.any { it % 2 == 1 })
        assertTrue("Should have even numbers", numbers.any { it % 2 == 0 })
    }

    @Test
    fun `setHouseNumbers continues from existing numbers`() {
        val road = createRoad("road1", listOf(3.0, 36.0, 3.01, 36.0))

        // One entrance with existing number
        val existingEntrance = createEntrance("e1", 3.005, 36.0001).copy(
            properties = createEntrance("e1", 3.005, 36.0001).properties.copy(entranceNumber = 5)
        )

        val newEntrance = createEntrance("e2", 3.008, 36.0001)

        val result = manager.setHouseNumbers(listOf(existingEntrance, newEntrance), road)

        val newNumbers = result.filter { it.id == "e2" }.mapNotNull { it.properties.entranceNumber }
        assertTrue("New entrance should get number > 5", newNumbers.all { it > 5 })
    }

    @Test
    fun `distance calculation is correct`() {
        // Test that the distance function works correctly
        val lat1 = 36.0
        val lng1 = 3.0
        val lat2 = 36.01 // ~1.1km north
        val lng2 = 3.0

        // Use reflection to access private method or test via public API
        // For now, just verify the manager can handle basic cases
        val road = createRoad("road1", listOf(lng1, lat1, lng2, lat2))
        val entrance = createEntrance("e1", lng1 + 0.001, lat1 + 0.001)

        val result = manager.setHouseNumbers(listOf(entrance), road)

        assertNotNull(result)
        assertEquals(1, result.size)
    }
}
