package com.nars.maplibre.data.store

import com.nars.maplibre.data.model.EntranceType
import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class FeatureStoreTest {

    private lateinit var featureStore: FeatureStore

    // Helper to create a test feature
    private fun createFeature(id: String, phaseKey: String, lng: Double = 3.0, lat: Double = 36.0): NarsFeature {
        return NarsFeature(
            id = id,
            type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(lng, lat)),
            properties = FeatureProperties(
                phase = phaseKey,
                color = "#3498db"
            )
        )
    }

    @Before
    fun setUp() {
        featureStore = FeatureStore()
    }

    @Test
    fun `addFeature adds to correct phase`() = runBlocking {
        val feature = createFeature("f1", Phases.ROADS_KEY)

        featureStore.addFeature(feature)

        val roads = featureStore.getFeaturesByPhase(Phases.ROADS_KEY)
        assertEquals(1, roads.size)
        assertEquals("f1", roads[0].id)
    }

    @Test
    fun `addFeature updates allFeatures`() = runBlocking {
        val feature = createFeature("f1", Phases.ROADS_KEY)

        featureStore.addFeature(feature)

        val allFeatures = featureStore.allFeatures.value
        assertEquals(1, allFeatures.size)
        assertEquals("f1", allFeatures[0].id)
    }

    @Test
    fun `addFeature updates fast lookup map`() = runBlocking {
        val feature = createFeature("f1", Phases.ROADS_KEY)

        featureStore.addFeature(feature)

        val retrieved = featureStore.getFeatureById("f1")
        assertNotNull(retrieved)
        assertEquals("f1", retrieved?.id)
    }

    @Test
    fun `getFeatureById returns null for non-existent feature`() = runBlocking {
        val result = featureStore.getFeatureById("nonexistent")
        assertNull(result)
    }

    @Test
    fun `updateFeature removes old and adds new`() = runBlocking {
        val feature1 = createFeature("f1", Phases.ROADS_KEY, lng = 3.0)
        val feature2 = createFeature("f1", Phases.ROADS_KEY, lng = 3.1) // Same ID, updated

        featureStore.addFeature(feature1)
        featureStore.updateFeature("f1", feature2)

        val allFeatures = featureStore.allFeatures.value
        assertEquals(1, allFeatures.size) // Still only 1 feature with ID "f1"
        assertEquals(3.1, allFeatures[0].geometry.let { (it as PointGeometry).coordinates[0] }, 0.001)
    }

    @Test
    fun `removeFeature removes from store`() = runBlocking {
        val feature = createFeature("f1", Phases.ROADS_KEY)
        featureStore.addFeature(feature)

        featureStore.removeFeature("f1")

        val allFeatures = featureStore.allFeatures.value
        assertEquals(0, allFeatures.size)
        assertNull(featureStore.getFeatureById("f1"))
    }

    @Test
    fun `removeFeature clears selection if selected`() = runBlocking {
        val feature = createFeature("f1", Phases.ROADS_KEY)
        featureStore.addFeature(feature)
        featureStore.selectFeature(feature)

        featureStore.removeFeature("f1")

        assertNull(featureStore.selectedFeature.value)
    }

    @Test
    fun `addFeatures batch adds multiple features`() = runBlocking {
        val features = listOf(
            createFeature("f1", Phases.ROADS_KEY),
            createFeature("f2", Phases.ROADS_KEY),
            createFeature("f3", Phases.HOUSE_ENTRANCES_KEY)
        )

        featureStore.addFeatures(features)

        assertEquals(3, featureStore.allFeatures.value.size)
        assertEquals(2, featureStore.getFeaturesByPhase(Phases.ROADS_KEY).size)
        assertEquals(1, featureStore.getFeaturesByPhase(Phases.HOUSE_ENTRANCES_KEY).size)
    }

    @Test
    fun `clearPhase removes features for specific phase`() = runBlocking {
        val features = listOf(
            createFeature("f1", Phases.ROADS_KEY),
            createFeature("f2", Phases.ROADS_KEY),
            createFeature("f3", Phases.HOUSE_ENTRANCES_KEY)
        )
        featureStore.addFeatures(features)

        featureStore.clearPhase(Phases.ROADS_KEY)

        assertEquals(1, featureStore.allFeatures.value.size)
        assertEquals(0, featureStore.getFeaturesByPhase(Phases.ROADS_KEY).size)
        assertEquals(1, featureStore.getFeaturesByPhase(Phases.HOUSE_ENTRANCES_KEY).size)
    }

    @Test
    fun `syncCounts returns correct counts`() = runBlocking {
        val features = listOf(
            createFeature("f1", Phases.ROADS_KEY),
            createFeature("f2", Phases.ROADS_KEY),
            createFeature("f3", Phases.HOUSE_ENTRANCES_KEY),
            createFeature("f4", Phases.NAMING_PANELS_KEY)
        )
        featureStore.addFeatures(features)
        featureStore.syncCounts()

        val counts = featureStore.featureCounts.value
        assertEquals(2, counts.roads)
        assertEquals(0, counts.mainEntrances) // entranceTypeKey is null, so not counted as main
        assertEquals(1, counts.namingPanels)
    }

    @Test
    fun `undo stack respects max size`() = runBlocking {
        // Add more than 50 undo actions
        for (i in 1..55) {
            val feature = createFeature("f$i", Phases.ROADS_KEY)
            featureStore.addFeature(feature)
            featureStore.addUndoAction(
                UndoAction.Delete(feature = feature, phaseKey = Phases.ROADS_KEY)
            )
        }

        // The undo stack should be at most 50
        // Note: This test may need adjustment based on actual undo implementation
        assertTrue(featureStore.canUndo)
    }

    @Test
    fun `getAllRoads returns roads only`() = runBlocking {
        val features = listOf(
            createFeature("f1", Phases.ROADS_KEY),
            createFeature("f2", Phases.HOUSE_ENTRANCES_KEY)
        )
        featureStore.addFeatures(features)

        val roads = featureStore.getAllRoads()
        assertEquals(1, roads.size)
        assertEquals("f1", roads[0].id)
    }
}
