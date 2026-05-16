package com.nars.maplibre.data.store

import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class FeatureStoreTest {

    private fun createRoad(id: String): NarsFeature = NarsFeature(
        id = id,
        type = NarsFeatureType.ROAD,
        geometry = LineStringGeometry(coordinates = listOf(3.0, 36.0, 3.01, 36.0)),
        properties = FeatureProperties(phase = Phases.ROADS_KEY, color = "#3498db", name = "Road $id")
    )

    private fun createEntrance(id: String): NarsFeature = NarsFeature(
        id = id,
        type = NarsFeatureType.HOUSE_ENTRANCE,
        geometry = PointGeometry(coordinates = listOf(3.0, 36.0)),
        properties = FeatureProperties(phase = Phases.HOUSE_ENTRANCES_KEY, color = "#27ae60")
    )

    @Test
    fun `addFeature stores feature and records undo`() {
        val store = FeatureStore()
        val feature = createRoad("r1")

        store.addFeature(feature, recordUndo = true)

        assertEquals(1, store.allFeatures.value.size)
        assertEquals(feature, store.allFeatures.value[0])
        assertTrue(store.canUndo)
    }

    @Test
    fun `addFeatures stores multiple features by phase`() {
        val store = FeatureStore()
        val road = createRoad("r1")
        val entrance = createEntrance("e1")

        store.addFeatures(listOf(road, entrance))

        assertEquals(2, store.allFeatures.value.size)
        assertEquals(1, store.getFeaturesByPhase(Phases.ROADS_KEY).size)
        assertEquals(1, store.getFeaturesByPhase(Phases.HOUSE_ENTRANCES_KEY).size)
    }

    @Test
    fun `addFeatures groups features by phase`() {
        val store = FeatureStore()
        val road1 = createRoad("r1")
        val road2 = createRoad("r2")

        store.addFeatures(listOf(road1, road2))

        assertEquals(2, store.getFeaturesByPhase(Phases.ROADS_KEY).size)
    }

    @Test
    fun `updateFeature modifies stored feature`() {
        val store = FeatureStore()
        val feature = createRoad("r1")
        store.addFeature(feature)

        val updated = feature.copy(properties = feature.properties.copy(name = "Updated Road"))
        store.updateFeature("r1", updated)

        assertEquals("Updated Road", store.getFeatureById("r1")?.properties?.name)
    }

    @Test
    fun `removeFeature deletes feature and clears selection`() {
        val store = FeatureStore()
        val feature = createRoad("r1")
        store.addFeature(feature)
        store.selectFeature(feature)

        store.removeFeature("r1")

        assertNull(store.getFeatureById("r1"))
        assertNull(store.selectedFeature.value)
        assertEquals(0, store.getFeaturesByPhase(Phases.ROADS_KEY).size)
    }

    @Test
    fun `setCurrentPhase updates current phase`() {
        val store = FeatureStore()
        store.setCurrentPhase(Phases.ALL[1])
        assertEquals(Phases.ALL[1], store.currentPhase.value)
    }

    @Test
    fun `getFeatureCounts returns correct counts`() {
        val store = FeatureStore()
        store.addFeatures(listOf(createRoad("r1"), createRoad("r2"), createEntrance("e1")))

        val counts = store.getFeatureCounts()
        assertEquals(2, counts[Phases.ROADS_KEY])
        assertEquals(1, counts[Phases.HOUSE_ENTRANCES_KEY])
    }

    @Test
    fun `clearAll removes all features`() {
        val store = FeatureStore()
        store.addFeatures(listOf(createRoad("r1"), createEntrance("e1")))
        store.selectFeature(createRoad("r1"))

        store.clearAll()

        assertEquals(0, store.allFeatures.value.size)
        assertNull(store.selectedFeature.value)
    }

    @Test
    fun `clearPhase removes only features of that phase`() {
        val store = FeatureStore()
        store.addFeatures(listOf(createRoad("r1"), createEntrance("e1")))

        store.clearPhase(Phases.ROADS_KEY)

        assertEquals(0, store.getFeaturesByPhase(Phases.ROADS_KEY).size)
        assertEquals(1, store.getFeaturesByPhase(Phases.HOUSE_ENTRANCES_KEY).size)
    }

    @Test
    fun `undo stack has max 50 items`() {
        val store = FeatureStore()
        for (i in 0 until 60) {
            store.addUndoAction(UndoAction.Create(createRoad("r$i"), Phases.ROADS_KEY))
        }

        assertTrue(store.canUndo)
        // 50 max, so pop 51 times should return null on the last
        for (i in 0 until 50) {
            assertNotNull(store.popUndoAction())
        }
        assertNull(store.popUndoAction())
    }

    @Test
    fun `getAllRoads returns only road features`() {
        val store = FeatureStore()
        store.addFeatures(listOf(createRoad("r1"), createEntrance("e1"), createRoad("r2")))

        val roads = store.getAllRoads()
        assertEquals(2, roads.size)
    }
}
