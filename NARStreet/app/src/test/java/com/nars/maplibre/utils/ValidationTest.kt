package com.nars.maplibre.utils

import com.nars.maplibre.data.model.FeatureProperties
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ValidationTest {

    @Test
    fun `validateRoadProperties returns failure when name is blank`() {
        val props = FeatureProperties.RoadProperties(
            name = ""
        )
        val result = validateFeatureProperties(props)
        assertFalse(result.valid)
        assertTrue(result.errors.containsKey("label"))
    }

    @Test
    fun `validateRoadProperties returns failure when roadType is blank`() {
        val props = FeatureProperties.RoadProperties(
            name = "Main Street"
        )
        val result = validateFeatureProperties(props)
        assertFalse(result.valid)
        assertTrue(result.errors.containsKey("roadType"))
    }

    @Test
    fun `validateRoadProperties returns failure when required fields missing`() {
        val props = FeatureProperties.RoadProperties(
            name = "Main Street",
            roadTypeKey = "street"
        )
        val result = validateFeatureProperties(props)
        assertFalse(result.valid)
        assertTrue(result.errors.containsKey("roadTraffic"))
        assertTrue(result.errors.containsKey("tradActivity"))
        assertTrue(result.errors.containsKey("numLanes"))
    }

    @Test
    fun `validateRoadProperties returns success when all fields filled`() {
        val props = FeatureProperties.RoadProperties(
            name = "Main Street",
            roadTypeKey = "street",
            roadTraffic = "low",
            tradActivity = "medium",
            numLanes = 2,
            hasMedian = false,
            hasVegetation = true,
            isDeadEnd = false,
            hasSidewalk = true
        )
        val result = validateFeatureProperties(props)
        assertTrue(result.valid)
    }

    @Test
    fun `validateHouseEntranceProperties requires roadDbId for main entrance`() {
        val props = FeatureProperties.HouseEntranceProperties(
            entranceTypeKey = "main_entrance",
            roadDbId = null
        )
        val result = validateFeatureProperties(props)
        assertFalse(result.valid)
        assertTrue(result.errors.containsKey("road"))
    }

    @Test
    fun `validateNamingPanelProperties requires name`() {
        val props = FeatureProperties.NamingPanelProperties(
            name = ""
        )
        val result = validateFeatureProperties(props)
        assertFalse(result.valid)
        assertTrue(result.errors.containsKey("label"))
    }

    @Test
    fun `validateHouseEntranceFieldWorkflow detects missing entrance`() {
        val props = FeatureProperties.HouseEntranceProperties(
            hasEntrance = false
        )
        val result = validateHouseEntranceFieldWorkflow(props)
        assertFalse(result.hasEntrance)
        assertTrue(result.needsNotification)
    }

    @Test
    fun `validateHouseEntranceFieldWorkflow full validation passes`() {
        val props = FeatureProperties.HouseEntranceProperties(
            hasEntrance = true,
            hasNumberingPanel = true,
            numberingPanelCorrect = true,
            numberingPanelPositionCorrect = true
        )
        val result = validateHouseEntranceFieldWorkflow(props)
        assertTrue(result.hasEntrance)
        assertFalse(result.needsNotification)
    }

    @Test
    fun `validateNamingPanelFieldWorkflow detects missing location`() {
        val props = FeatureProperties.NamingPanelProperties(
            hasNamingPanelLocation = false
        )
        val result = validateNamingPanelFieldWorkflow(props)
        assertFalse(result.hasLocation)
        assertTrue(result.needsNotification)
    }

    @Test
    fun `validateRoadLength fails for short road`() {
        val result = validateRoadLength(5.0)
        assertFalse(result.valid)
    }

    @Test
    fun `validateRoadLength succeeds for long enough road`() {
        val result = validateRoadLength(50.0)
        assertTrue(result.valid)
    }
}
