package com.nars.maplibre.modes

import com.geoman.maplibre.geoman.Geoman
import com.geoman.maplibre.geoman.core.features.FeatureData
import com.geoman.maplibre.geoman.types.geojson.LineString
import com.geoman.maplibre.geoman.types.geojson.LngLat
import com.geoman.maplibre.geoman.types.geojson.MultiPolygon
import com.geoman.maplibre.geoman.types.geojson.Point
import com.geoman.maplibre.geoman.types.geojson.Polygon
import com.nars.maplibre.data.model.CircleGeometry
import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import io.mockk.Runs
import io.mockk.every
import io.mockk.just
import io.mockk.mockk
import io.mockk.slot
import io.mockk.verify
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class GeomanEventHandlerTest {

    private lateinit var geoman: Geoman
    private lateinit var onFeatureCreated: (NarsFeature) -> Unit
    private lateinit var onFeatureUpdated: (NarsFeature) -> Unit
    private lateinit var onFeatureDeleted: (String) -> Unit
    private lateinit var handler: GeomanEventHandler

    private val roadPhase = PhaseDefinition(0, Phases.ROADS_KEY, "roads", com.nars.maplibre.data.model.DrawType.POLYLINE, "#3498db", "")
    private val entrancePhase = PhaseDefinition(1, Phases.HOUSE_ENTRANCES_KEY, "entrances", com.nars.maplibre.data.model.DrawType.MARKER, "#27ae60", "")
    private val panelPhase = PhaseDefinition(2, Phases.NAMING_PANELS_KEY, "panels", com.nars.maplibre.data.model.DrawType.MARKER, "#9b59b6", "")

    @Before
    fun setUp() {
        geoman = mockk(relaxed = true)
        onFeatureCreated = mockk(relaxed = true)
        onFeatureUpdated = mockk(relaxed = true)
        onFeatureDeleted = mockk(relaxed = true)
        handler = GeomanEventHandler(mockk(), geoman, onFeatureCreated, onFeatureUpdated, onFeatureDeleted)
    }

    private fun createFeature(): NarsFeature {
        return NarsFeature(
            id = "f1", type = NarsFeatureType.ROAD,
            geometry = PointGeometry(coordinates = listOf(1.0, 2.0)),
            properties = FeatureProperties(phase = Phases.ROADS_KEY, color = "#3498db")
        )
    }

    // --- State management ---

    @Test
    fun `getEditingFeatureId returns null initially`() {
        assertNull(handler.getEditingFeatureId())
    }

    @Test
    fun `getEditingFeature returns null initially`() {
        assertNull(handler.getEditingFeature())
    }

    @Test
    fun `setEditingFeature stores id and feature`() {
        val feature = createFeature()
        handler.setEditingFeature("f1", feature)
        assertEquals("f1", handler.getEditingFeatureId())
        assertEquals(feature, handler.getEditingFeature())
    }

    @Test
    fun `setEditingFeature with null clears state`() {
        val feature = createFeature()
        handler.setEditingFeature("f1", feature)
        handler.setEditingFeature(null, null)
        assertNull(handler.getEditingFeatureId())
        assertNull(handler.getEditingFeature())
    }

    // --- getFeatureTypeFromPhase ---

    @Test
    fun `getFeatureTypeFromPhase returns ROAD for roads key`() {
        assertEquals(NarsFeatureType.ROAD, handler.getFeatureTypeFromPhase(roadPhase))
    }

    @Test
    fun `getFeatureTypeFromPhase returns HOUSE_ENTRANCE for houseEntrances key`() {
        assertEquals(NarsFeatureType.HOUSE_ENTRANCE, handler.getFeatureTypeFromPhase(entrancePhase))
    }

    @Test
    fun `getFeatureTypeFromPhase returns NAMING_PANEL for namingPanels key`() {
        assertEquals(NarsFeatureType.NAMING_PANEL, handler.getFeatureTypeFromPhase(panelPhase))
    }

    @Test
    fun `getFeatureTypeFromPhase defaults to ROAD for unknown key`() {
        val unknown = PhaseDefinition(99, "unknown", "?", com.nars.maplibre.data.model.DrawType.POLYGON, "#000", "")
        assertEquals(NarsFeatureType.ROAD, handler.getFeatureTypeFromPhase(unknown))
    }

    // --- extractGeometryFromFeatureData ---

    @Test
    fun `extract with Point returns PointGeometry`() {
        val point = mockk<Point>(relaxed = true)
        every { point.coordinates } returns listOf(1.0, 2.0)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns point

        val result = handler.extractGeometryFromFeatureData(featureData)
        assertEquals(PointGeometry(coordinates = listOf(1.0, 2.0)), result)
    }

    @Test
    fun `extract with LineString returns LineStringGeometry`() {
        val lineString = mockk<LineString>(relaxed = true)
        every { lineString.coordinates } returns listOf(listOf(1.0, 2.0), listOf(3.0, 4.0))
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns lineString

        val result = handler.extractGeometryFromFeatureData(featureData)
        assertEquals(LineStringGeometry(coordinates = listOf(1.0, 2.0, 3.0, 4.0)), result)
    }

    @Test
    fun `extract with Polygon returns PolygonGeometry`() {
        val polygon = mockk<Polygon>(relaxed = true)
        val exteriorRing = listOf(listOf(1.0, 2.0), listOf(3.0, 4.0), listOf(5.0, 6.0), listOf(1.0, 2.0))
        every { polygon.coordinates } returns listOf(exteriorRing)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns polygon

        val result = handler.extractGeometryFromFeatureData(featureData)
        assertEquals(PolygonGeometry(coordinates = listOf(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 1.0, 2.0)), result)
    }

    @Test
    fun `extract with empty Polygon returns empty PolygonGeometry`() {
        val polygon = mockk<Polygon>(relaxed = true)
        every { polygon.coordinates } returns emptyList<List<List<Double>>>()
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns polygon

        val result = handler.extractGeometryFromFeatureData(featureData)
        assertEquals(PolygonGeometry(coordinates = emptyList()), result)
    }

    @Test
    fun `extract with MultiPolygon returns PolygonGeometry`() {
        val multiPolygon = mockk<MultiPolygon>(relaxed = true)
        val exteriorRing = listOf(listOf(1.0, 2.0), listOf(3.0, 4.0), listOf(1.0, 2.0))
        every { multiPolygon.coordinates } returns listOf(listOf(exteriorRing))
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns multiPolygon

        val result = handler.extractGeometryFromFeatureData(featureData)
        assertEquals(PolygonGeometry(coordinates = listOf(1.0, 2.0, 3.0, 4.0, 1.0, 2.0)), result)
    }

    @Test
    fun `extract with unknown geometry returns fallback PointGeometry`() {
        val unknown = mockk<com.geoman.maplibre.geoman.types.geojson.Geometry>(relaxed = true)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns unknown

        val result = handler.extractGeometryFromFeatureData(featureData)
        assertEquals(PointGeometry(coordinates = listOf(0.0, 0.0)), result)
    }

    // --- extractCircleGeometry ---

    @Test
    fun `extractCircleGeometry with center and radius returns circle`() {
        val center = mockk<LngLat>(relaxed = true)
        every { center.longitude } returns 1.0
        every { center.latitude } returns 2.0
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.properties } returns mutableMapOf("center" to center, "radius" to 50.0)

        val result = handler.extractCircleGeometry(featureData)
        assertEquals(CircleGeometry(coordinates = listOf(1.0, 2.0, 50.0)), result)
    }

    @Test
    fun `extractCircleGeometry without center or radius falls back`() {
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.properties } returns mutableMapOf<String, Any?>()
        every { featureData.geometry } returns mockk(relaxed = true)

        val result = handler.extractCircleGeometry(featureData)
        assertTrue(result is CircleGeometry)
    }

    @Test
    fun `extractCircleGeometry with null properties returns fallback`() {
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.properties } returns mutableMapOf()
        every { featureData.geometry } returns mockk(relaxed = true)

        val result = handler.extractCircleGeometry(featureData)
        assertTrue(result is CircleGeometry)
    }

    // --- handleFeatureCreated ---

    @Test
    fun `handleFeatureCreated with phase and feature data calls onFeatureCreated`() {
        handler.setCurrentPhase(roadPhase)

        val point = mockk<Point>(relaxed = true)
        every { point.coordinates } returns listOf(1.0, 2.0)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.id } returns "f1"
        every { featureData.properties } returns mutableMapOf<String, Any?>()
        every { featureData.geometry } returns point

        handler.handleFeatureCreated(featureData)

        verify { onFeatureCreated(any()) }
    }

    @Test
    fun `handleFeatureCreated without phase does not call onFeatureCreated`() {
        val point = mockk<Point>(relaxed = true)
        every { point.coordinates } returns listOf(1.0, 2.0)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.id } returns "f1"
        every { featureData.properties } returns mutableMapOf<String, Any?>()
        every { featureData.geometry } returns point

        handler.handleFeatureCreated(featureData)

        verify(exactly = 0) { onFeatureCreated(any()) }
    }

    @Test
    fun `handleFeatureCreated with null feature data creates feature with UUID`() {
        handler.setCurrentPhase(roadPhase)

        handler.handleFeatureCreated(null)

        verify { onFeatureCreated(any()) }
    }

    @Test
    fun `handleFeatureCreated with circle feature data creates circle feature`() {
        handler.setCurrentPhase(roadPhase)

        val center = mockk<LngLat>(relaxed = true)
        every { center.longitude } returns 1.0
        every { center.latitude } returns 2.0
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.id } returns "f1"
        every { featureData.properties } returns mutableMapOf("center" to center, "radius" to 50.0)
        every { featureData.geometry } returns mockk(relaxed = true)

        handler.handleFeatureCreated(featureData)

        verify { onFeatureCreated(any()) }
    }

    // --- createNarsFeatureFromFeatureData ---

    @Test
    fun `createNarsFeatureFromFeatureData returns feature with correct phase type`() {
        val point = mockk<Point>(relaxed = true)
        every { point.coordinates } returns listOf(10.0, 20.0)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.id } returns "f2"
        every { featureData.properties } returns mutableMapOf<String, Any?>()
        every { featureData.geometry } returns point

        val result = handler.createNarsFeatureFromFeatureData(featureData, roadPhase)

        assertEquals("f2", result.id)
        assertEquals(NarsFeatureType.ROAD, result.type)
        assertEquals(Phases.ROADS_KEY, result.properties.phase)
        assertEquals("#3498db", result.properties.color)
    }

    @Test
    fun `createNarsFeatureFromFeatureData with null data uses fallback geometry`() {
        val result = handler.createNarsFeatureFromFeatureData(null, roadPhase)

        assertEquals(NarsFeatureType.ROAD, result.type)
        assertEquals(PointGeometry(coordinates = listOf(0.0, 0.0)), result.geometry)
    }

    // --- handleEditEnd ---

    @Test
    fun `handleEditEnd clears editing state`() {
        handler.setEditingFeature("f1", createFeature())
        handler.handleEditEnd()
        assertNull(handler.getEditingFeatureId())
        assertNull(handler.getEditingFeature())
    }

    // --- handleGeometryChanged ---

    @Test
    fun `handleGeometryChanged with editing feature calls onFeatureUpdated`() {
        handler.setEditingFeature("f1", createFeature())

        val point = mockk<Point>(relaxed = true)
        every { point.coordinates } returns listOf(3.0, 4.0)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns point

        handler.handleGeometryChanged(featureData)

        verify { onFeatureUpdated(any()) }
    }

    @Test
    fun `handleGeometryChanged without editing feature does not call onFeatureUpdated`() {
        val point = mockk<Point>(relaxed = true)
        every { point.coordinates } returns listOf(3.0, 4.0)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { featureData.geometry } returns point

        handler.handleGeometryChanged(featureData)

        verify(exactly = 0) { onFeatureUpdated(any()) }
    }

    @Test
    fun `handleGeometryChanged with null feature data does not call onFeatureUpdated`() {
        handler.setEditingFeature("f1", createFeature())
        handler.handleGeometryChanged(null)

        verify(exactly = 0) { onFeatureUpdated(any()) }
    }

    // --- handleDeleted ---

    @Test
    fun `handleDeleted with editing feature calls onFeatureDeleted and clears state`() {
        handler.setEditingFeature("f1", createFeature())

        handler.handleDeleted()

        verify { onFeatureDeleted("f1") }
        assertNull(handler.getEditingFeatureId())
        assertNull(handler.getEditingFeature())
    }

    @Test
    fun `handleDeleted without editing feature does not call onFeatureDeleted`() {
        handler.handleDeleted()
        verify(exactly = 0) { onFeatureDeleted(any()) }
    }
}
