package com.nars.maplibre.utils

import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.store.FeatureStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PhaseNavigatorTest {

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

    // ── canAdvance ────────────────────────────────────────────────────────

    @Test
    fun `canAdvance returns null when roads exist and advancing from roads`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.canAdvance(1)
        assertNull(result)
    }

    @Test
    fun `canAdvance returns error when target phase invalid`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.canAdvance(99)
        assertEquals("alert_invalid_phase", result)
    }

    @Test
    fun `canAdvance returns null when target is behind current phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[1])
        val result = navigator.canAdvance(0)
        assertNull(result)
    }

    @Test
    fun `canAdvance returns null when target is same as current phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.canAdvance(0)
        assertNull(result)
    }

    @Test
    fun `canAdvance blocks advancing from roads when no roads exist`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.canAdvance(1)
        assertEquals("alert_at_least_one_road", result)
    }

    @Test
    fun `canAdvance allows advancing from roads when roads exist`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.canAdvance(1)
        assertNull(result)
    }

    @Test
    fun `canAdvance blocks advancing from houseEntrances when no entrances exist`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        store.setCurrentPhase(Phases.ALL[1])
        val result = navigator.canAdvance(2)
        assertEquals("alert_at_least_one_entrance", result)
    }

    @Test
    fun `canAdvance allows advancing from houseEntrances when entrances exist`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        store.addFeature(createEntrance("e1"))
        store.setCurrentPhase(Phases.ALL[1])
        val result = navigator.canAdvance(2)
        assertNull(result)
    }

    @Test
    fun `canAdvance allows going backward from last phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[2])
        val result = navigator.canAdvance(1)
        assertNull(result)
    }

    // ── navigateTo ────────────────────────────────────────────────────────

    @Test
    fun `navigateTo returns null when blocked`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.navigateTo(1)
        assertNull(result)
    }

    @Test
    fun `navigateTo returns target phase when allowed`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.navigateTo(1)
        assertNotNull(result)
        assertEquals(Phases.ALL[1], result)
    }

    // ── getPreviousPhaseIndex ─────────────────────────────────────────────

    @Test
    fun `getPreviousPhaseIndex returns null when at first phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        assertNull(navigator.getPreviousPhaseIndex())
    }

    @Test
    fun `getPreviousPhaseIndex returns index - 1 when not at first phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[1])
        assertEquals(0, navigator.getPreviousPhaseIndex())
    }

    @Test
    fun `getPreviousPhaseIndex returns index - 1 for last phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[2])
        assertEquals(1, navigator.getPreviousPhaseIndex())
    }

    // ── getNextPhaseIndex ─────────────────────────────────────────────────

    @Test
    fun `getNextPhaseIndex returns null when at last phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[2])
        assertNull(navigator.getNextPhaseIndex())
    }

    @Test
    fun `getNextPhaseIndex returns index + 1 when not at last phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        assertEquals(1, navigator.getNextPhaseIndex())
    }

    @Test
    fun `getNextPhaseIndex returns index + 1 for middle phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[1])
        assertEquals(2, navigator.getNextPhaseIndex())
    }

    // ── canGoBack ─────────────────────────────────────────────────────────

    @Test
    fun `canGoBack returns false at first phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        assertFalse(navigator.canGoBack())
    }

    @Test
    fun `canGoBack returns true at non-first phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[1])
        assertTrue(navigator.canGoBack())
    }

    // ── canGoForward ──────────────────────────────────────────────────────

    @Test
    fun `canGoForward returns false at last phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[2])
        assertFalse(navigator.canGoForward())
    }

    @Test
    fun `canGoForward returns false when phase requirements not met`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        assertFalse(navigator.canGoForward())
    }

    @Test
    fun `canGoForward returns true when phase requirements met`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        store.setCurrentPhase(Phases.ALL[0])
        assertTrue(navigator.canGoForward())
    }

    // ── goBack ────────────────────────────────────────────────────────────

    @Test
    fun `goBack returns null at first phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        assertNull(navigator.goBack())
    }

    @Test
    fun `goBack navigates to previous phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[1])
        val result = navigator.goBack()
        assertEquals(Phases.ALL[0], result)
        assertEquals(Phases.ALL[0], store.currentPhase.value)
    }

    // ── goNext ────────────────────────────────────────────────────────────

    @Test
    fun `goNext returns null at last phase`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[2])
        assertNull(navigator.goNext())
    }

    @Test
    fun `goNext returns null when phase requirements not met`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.setCurrentPhase(Phases.ALL[0])
        assertNull(navigator.goNext())
    }

    @Test
    fun `goNext navigates to next phase when requirements met`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        store.setCurrentPhase(Phases.ALL[0])
        val result = navigator.goNext()
        assertEquals(Phases.ALL[1], result)
        assertEquals(Phases.ALL[1], store.currentPhase.value)
    }

    // ── checkRoadCoverage ──────────────────────────────────────────────────

    @Test
    fun `checkRoadCoverage returns uncovered when no roads`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        val result = navigator.checkRoadCoverage()
        assertFalse(result.covered)
        assertEquals("No roads defined", result.message)
    }

    @Test
    fun `checkRoadCoverage returns covered when roads exist`() {
        val store = FeatureStore()
        val navigator = PhaseNavigator(store)
        store.addFeature(createRoad("r1"))
        val result = navigator.checkRoadCoverage()
        assertTrue(result.covered)
        assertEquals("OK", result.message)
    }

    // ── Edge cases ─────────────────────────────────────────────────────────


}
