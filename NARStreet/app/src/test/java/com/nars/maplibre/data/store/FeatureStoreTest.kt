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
        properties = FeatureProperties.RoadProperties(name = "Road $id")
    )

    private fun createEntrance(id: String): NarsFeature = NarsFeature(
        id = id,
        type = NarsFeatureType.HOUSE_ENTRANCE,
        geometry = PointGeometry(coordinates = listOf(3.0, 36.0)),
        properties = FeatureProperties.HouseEntranceProperties()
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

        val updatedProps = (feature.properties as FeatureProperties.RoadProperties).copy(name = "Updated Road")
        val updated = feature.copy(properties = updatedProps)
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

    // ── executeUndo ───────────────────────────────────────────────────────────

    @Test
    fun `executeUndo returns null when stack is empty`() {
        val store = FeatureStore()
        assertNull(store.executeUndo())
    }

    @Test
    fun `executeUndo with Create removes the feature`() {
        val store = FeatureStore()
        val feature = createRoad("r1")
        store.addFeature(feature, recordUndo = true)
        assertNotNull(store.getFeatureById("r1"))

        val action = store.executeUndo()
        assertTrue(action is UndoAction.Create)
        assertNull(store.getFeatureById("r1"))
    }

    @Test
    fun `executeUndo with Update restores old feature`() {
        val store = FeatureStore()
        val original = createRoad("r1")
        store.addFeature(original)

        val updatedProps = (original.properties as FeatureProperties.RoadProperties).copy(name = "Renamed")
        val updated = original.copy(properties = updatedProps)
        store.updateFeature("r1", updated)
        store.addUndoAction(UndoAction.Update(oldFeature = original, newFeature = updated, phaseKey = Phases.ROADS_KEY))

        val action = store.executeUndo()
        assertTrue(action is UndoAction.Update)
        assertEquals("Road r1", store.getFeatureById("r1")?.properties?.name)
    }

    @Test
    fun `executeUndo with Delete restores the feature`() {
        val store = FeatureStore()
        val feature = createRoad("r1")
        store.addFeature(feature)
        store.addUndoAction(UndoAction.Delete(feature = feature, phaseKey = Phases.ROADS_KEY))

        store.removeFeature("r1")
        assertNull(store.getFeatureById("r1"))

        val action = store.executeUndo()
        assertTrue(action is UndoAction.Delete)
        assertNotNull(store.getFeatureById("r1"))
    }

    @Test
    fun `executeUndo with Delete does not re-add when feature still exists`() {
        val store = FeatureStore()
        val feature = createRoad("r1")
        store.addFeature(feature)
        store.addUndoAction(UndoAction.Delete(feature = feature, phaseKey = Phases.ROADS_KEY))

        store.executeUndo()
        // Re-add should not duplicate
        assertEquals(1, store.getFeaturesByPhase(Phases.ROADS_KEY).size)
    }

    // ── referenceRoadDbId ─────────────────────────────────────────────────────

    @Test
    fun `setReferenceRoad updates referenceRoadDbId`() {
        val store = FeatureStore()
        assertNull(store.referenceRoadDbId.value)

        store.setReferenceRoad("road-db-42")
        assertEquals("road-db-42", store.referenceRoadDbId.value)

        store.setReferenceRoad(null)
        assertNull(store.referenceRoadDbId.value)
    }

    // ── getCurrentPhaseFeatures ───────────────────────────────────────────────

    @Test
    fun `getCurrentPhaseFeatures returns features for current phase`() {
        val store = FeatureStore()
        store.addFeatures(listOf(createRoad("r1"), createEntrance("e1")))
        store.setCurrentPhase(Phases.ALL[0])

        val currentFeatures = store.getCurrentPhaseFeatures()
        assertEquals(1, currentFeatures.size)
        assertEquals(Phases.ROADS_KEY, currentFeatures[0].properties.phase)
    }

    @Test
    fun `getCurrentPhaseFeatures returns empty when no features`() {
        val store = FeatureStore()
        assertTrue(store.getCurrentPhaseFeatures().isEmpty())
    }

    // ── setCurrentPhaseByKey ─────────────────────────────────────────────────

    @Test
    fun `setCurrentPhaseByKey sets phase from key`() {
        val store = FeatureStore()
        store.setCurrentPhaseByKey(Phases.NAMING_PANELS_KEY)
        assertEquals(Phases.NAMING_PANELS_KEY, store.currentPhase.value?.key)
    }

    @Test
    fun `setCurrentPhaseByKey does nothing for unknown key`() {
        val store = FeatureStore()
        store.setCurrentPhaseByKey("nonexistent")
        // default is first phase (roads)
        assertEquals(Phases.ROADS_KEY, store.currentPhase.value?.key)
    }

    // ── addFeature without undo ───────────────────────────────────────────────

    @Test
    fun `addFeature without undo does not record action`() {
        val store = FeatureStore()
        store.addFeature(createRoad("r1"), recordUndo = false)
        assertFalse(store.canUndo)
    }

    // ── featureCounts mapping ────────────────────────────────────────────────

    @Test
    fun `getFeatureCounts returns empty map when no features`() {
        val store = FeatureStore()
        assertTrue(store.getFeatureCounts().isEmpty())
    }

    @Test
    fun `getFeatureCounts handles phases without features`() {
        val store = FeatureStore()
        store.addFeature(createRoad("r1"))
        val counts = store.getFeatureCounts()
        assertEquals(1, counts[Phases.ROADS_KEY])
        assertNull(counts[Phases.HOUSE_ENTRANCES_KEY])
    }

    // ── getFeatureById edge cases ─────────────────────────────────────────────

    @Test
    fun `getFeatureById returns null for unknown id`() {
        val store = FeatureStore()
        assertNull(store.getFeatureById("nonexistent"))
    }

    @Test
    fun `getFeatureById returns feature after addFeatures`() {
        val store = FeatureStore()
        store.addFeatures(listOf(createRoad("r1"), createRoad("r2")))
        assertNotNull(store.getFeatureById("r1"))
        assertNotNull(store.getFeatureById("r2"))
    }

    // ── selectFeature ─────────────────────────────────────────────────────────

    @Test
    fun `selectFeature updates selected feature`() {
        val store = FeatureStore()
        val feature = createRoad("r1")
        store.selectFeature(feature)
        assertEquals(feature, store.selectedFeature.value)

        store.selectFeature(null)
        assertNull(store.selectedFeature.value)
    }

    // ── removeFeature edge cases ──────────────────────────────────────────────

    @Test
    fun `removeFeature does nothing for unknown id`() {
        val store = FeatureStore()
        store.addFeature(createRoad("r1"))
        store.removeFeature("nonexistent")
        assertEquals(1, store.allFeatures.value.size)
    }
}
