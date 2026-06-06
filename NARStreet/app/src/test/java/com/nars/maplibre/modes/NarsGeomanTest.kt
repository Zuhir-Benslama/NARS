package com.nars.maplibre.modes

import com.geoman.maplibre.geoman.Geoman
import com.geoman.maplibre.geoman.core.GeomanCoreConstants
import com.geoman.maplibre.geoman.core.features.FeatureData
import com.geoman.maplibre.geoman.types.DrawModeName
import com.geoman.maplibre.geoman.types.EditModeName
import com.geoman.maplibre.geoman.types.ModeType
import com.geoman.maplibre.geoman.types.geojson.Feature
import com.nars.maplibre.data.model.CircleGeometry
import com.nars.maplibre.data.model.DrawType
import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.PhaseDefinition
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import io.mockk.Called
import io.mockk.Runs
import io.mockk.every
import io.mockk.just
import io.mockk.mockk
import io.mockk.slot
import io.mockk.verify
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.maplibre.android.geometry.LatLng

class NarsGeomanTest {

    private lateinit var geoman: Geoman
    private lateinit var featureRenderer: FeatureRenderer
    private lateinit var eventHandler: GeomanEventHandler
    private lateinit var geometryConverter: GeometryConverter
    private lateinit var snappingEngine: SnappingEngine
    private lateinit var labelAndMarkerManager: LabelAndMarkerManager
    private lateinit var narsGeoman: NarsGeoman

    private val onFeatureCreated: (NarsFeature) -> Unit = mockk()
    private val onFeatureUpdated: (NarsFeature) -> Unit = mockk()
    private val onFeatureDeleted: (String) -> Unit = mockk()

    private val pointPhase = PhaseDefinition(0, Phases.ROADS_KEY, "roads", DrawType.POLYLINE, "#3498db", "")
    private val markerPhase = PhaseDefinition(2, Phases.NAMING_PANELS_KEY, "panels", DrawType.MARKER, "#9b59b6", "")

    @Before
    fun setUp() {
        geoman = mockk(relaxed = true)
        featureRenderer = mockk(relaxed = true)
        eventHandler = mockk(relaxed = true)
        geometryConverter = mockk(relaxed = true)
        snappingEngine = mockk(relaxed = true)
        labelAndMarkerManager = mockk(relaxed = true)
        every { featureRenderer.labelAndMarkerManager } returns labelAndMarkerManager

        narsGeoman = NarsGeoman(
            geoman = geoman,
            featureRenderer = featureRenderer,
            eventHandler = eventHandler,
            geometryConverter = geometryConverter,
            snappingEngine = snappingEngine,
            onFeatureCreated = onFeatureCreated,
            onFeatureUpdated = onFeatureUpdated,
            onFeatureDeleted = onFeatureDeleted
        )
    }

    @After
    fun tearDown() {
        narsGeoman.destroy()
    }

    private fun createRoad(name: String? = null, geometry: com.nars.maplibre.data.model.Geometry = PointGeometry(coordinates = listOf(3.0, 36.0))): NarsFeature {
        return NarsFeature(
            id = "road-1",
            type = NarsFeatureType.ROAD,
            geometry = geometry,
            properties = FeatureProperties(phase = Phases.ROADS_KEY, color = "#3498db", name = name)
        )
    }

    // --- setCurrentPhase ---

    @Test
    fun `setCurrentPhase delegates to eventHandler`() {
        narsGeoman.setCurrentPhase(pointPhase)
        verify { eventHandler.setCurrentPhase(pointPhase) }
    }

    // --- startDrawing ---

    @Test
    fun `startDrawing does nothing when no phase set`() {
        narsGeoman.startDrawing()
        verify { geoman wasNot Called }
        verify { eventHandler wasNot Called }
    }

    @Test
    fun `startDrawing enables polyline for POLYLINE draw type`() {
        narsGeoman.setCurrentPhase(pointPhase)
        narsGeoman.startDrawing()
        verify { geoman.disableAllModes() }
        verify { eventHandler.setEditingFeature(null, null) }
        verify { geoman.enableDraw(DrawModeName.LINE) }
        assertTrue(narsGeoman.isDrawing.value)
        assertFalse(narsGeoman.isEditing.value)
    }

    @Test
    fun `startDrawing enables marker for MARKER draw type`() {
        narsGeoman.setCurrentPhase(markerPhase)
        narsGeoman.startDrawing()
        verify { geoman.enableDraw(DrawModeName.MARKER) }
    }

    @Test
    fun `startDrawing enables polygon for POLYGON draw type`() {
        val polygonPhase = PhaseDefinition(0, "poly", "poly", DrawType.POLYGON, "#000", "")
        narsGeoman.setCurrentPhase(polygonPhase)
        narsGeoman.startDrawing()
        verify { geoman.enableDraw(DrawModeName.POLYGON) }
    }

    @Test
    fun `startDrawing enables circle for CIRCLE draw type`() {
        val circlePhase = PhaseDefinition(0, "circ", "circ", DrawType.CIRCLE, "#000", "")
        narsGeoman.setCurrentPhase(circlePhase)
        narsGeoman.startDrawing()
        verify { geoman.enableDraw(DrawModeName.CIRCLE) }
    }

    // --- stopDrawing ---

    @Test
    fun `stopDrawing resets state and disables modes`() {
        narsGeoman.setCurrentPhase(pointPhase)
        narsGeoman.startDrawing()
        narsGeoman.stopDrawing()
        assertFalse(narsGeoman.isDrawing.value)
        verify { geoman.disableAllModes() }
    }

    // --- startEditing ---

    @Test
    fun `startEditing configures editing state`() {
        val feature = createRoad("Main")
        val geoJsonFeature = mockk<Feature>(relaxed = true)
        val featureData = mockk<FeatureData>(relaxed = true)
        every { geometryConverter.convertToGeoJson(feature) } returns geoJsonFeature
        every { geometryConverter.convertToGeomanFeatureData(feature) } returns featureData

        narsGeoman.startEditing(feature)

        verify { geoman.disableAllModes() }
        verify { eventHandler.setEditingFeature(feature.id, feature) }
        verify { geoman.addGeoJsonFeature(geoJsonFeature) }
        verify { geoman.enableEdit(EditModeName.CHANGE) }
        verify { geoman.startEditingFeature(featureData) }
        assertTrue(narsGeoman.isEditing.value)
        assertFalse(narsGeoman.isDrawing.value)
    }

    // --- stopEditing ---

    @Test
    fun `stopEditing resets editing state`() {
        narsGeoman.stopEditing()
        assertFalse(narsGeoman.isEditing.value)
        verify { eventHandler.setEditingFeature(null, null) }
        verify { geoman.disableAllModes() }
    }

    // --- commitEdits ---

    @Test
    fun `commitEdits does nothing when no editing feature`() {
        every { eventHandler.getEditingFeature() } returns null
        narsGeoman.commitEdits()
        verify { onFeatureUpdated wasNot Called }
    }

    @Test
    fun `commitEdits finds updated geometry and calls callback`() {
        val original = createRoad("Road", geometry = LineStringGeometry(coordinates = listOf(3.0, 36.0, 3.1, 36.1)))
        every { eventHandler.getEditingFeature() } returns original

        val featureData = mockk<FeatureData>(relaxed = true)
        every { geoman.features.getFeature(GeomanCoreConstants.SOURCE_MARKERS, original.id) } returns null
        every { geoman.features.getFeature(GeomanCoreConstants.SOURCE_LINES, original.id) } returns featureData

        val updatedGeometry = LineStringGeometry(coordinates = listOf(3.0, 37.0, 3.1, 37.1))
        every { eventHandler.extractGeometryFromFeatureData(featureData) } returns updatedGeometry

        val capturedFeature = slot<NarsFeature>()
        every { onFeatureUpdated(capture(capturedFeature)) } just Runs

        narsGeoman.commitEdits()

        assertEquals(updatedGeometry, capturedFeature.captured.geometry)
        verify { onFeatureUpdated(any()) }
        assertFalse(narsGeoman.isEditing.value)
    }

    @Test
    fun `commitEdits falls back to original when geometry not found`() {
        val original = createRoad("Road")
        every { eventHandler.getEditingFeature() } returns original
        every { geoman.features.getFeature(any(), original.id) } returns null

        val capturedFeature = slot<NarsFeature>()
        every { onFeatureUpdated(capture(capturedFeature)) } just Runs

        narsGeoman.commitEdits()

        assertEquals(original.geometry, capturedFeature.captured.geometry)
    }

    // --- cancelEdits ---

    @Test
    fun `cancelEdits does nothing when no editing feature id`() {
        every { eventHandler.getEditingFeatureId() } returns null
        narsGeoman.cancelEdits()
        verify(exactly = 0) { geoman.features.getFeature(any(), any()) }
    }

    @Test
    fun `cancelEdits removes feature from geoman and stops editing`() {
        every { eventHandler.getEditingFeatureId() } returns "road-1"
        val featureData = mockk<FeatureData>(relaxed = true)
        every { geoman.features.getFeature(GeomanCoreConstants.SOURCE_MARKERS, "road-1") } returns featureData

        narsGeoman.cancelEdits()

        verify { geoman.features.removeFeature(GeomanCoreConstants.SOURCE_MARKERS, "road-1") }
        assertFalse(narsGeoman.isEditing.value)
    }

    // --- addFeature ---

    @Test
    fun `addFeature delegates to renderer and geoman`() {
        val feature = createRoad("Road")
        val geoJson = mockk<Feature>(relaxed = true)
        every { geometryConverter.convertToGeoJson(feature) } returns geoJson
        every { geometryConverter.getSourceNameForGeometry(feature.geometry) } returns "nars_source"

        narsGeoman.addFeature(feature)

        verify { featureRenderer.addFeature(feature) }
        verify { geoman.addGeoJsonFeature(geoJson, "nars_source") }
    }

    // --- addFeatures ---

    @Test
    fun `addFeatures with no current phase adds all features`() {
        val f1 = createRoad("R1")
        val f2 = NarsFeature(
            id = "hp-1",
            type = NarsFeatureType.HOUSE_ENTRANCE,
            geometry = PointGeometry(coordinates = listOf(3.0, 36.1)),
            properties = FeatureProperties(phase = Phases.HOUSE_ENTRANCES_KEY, color = "#27ae60")
        )

        narsGeoman.addFeatures(listOf(f1, f2))

        verify { featureRenderer.addFeature(f1) }
        verify { featureRenderer.addFeature(f2) }
    }

    @Test
    fun `addFeatures with phase filters by phase key`() {
        val f1 = createRoad("R1")
        val f2 = NarsFeature(
            id = "hp-1",
            type = NarsFeatureType.HOUSE_ENTRANCE,
            geometry = PointGeometry(coordinates = listOf(3.0, 36.1)),
            properties = FeatureProperties(phase = Phases.HOUSE_ENTRANCES_KEY, color = "#27ae60")
        )

        narsGeoman.setCurrentPhase(pointPhase)
        narsGeoman.addFeatures(listOf(f1, f2))

        verify { featureRenderer.addFeature(f1) }
    }

    // --- updateDisplayedFeatures ---

    @Test
    fun `updateDisplayedFeatures adds new features and removes stale ones`() {
        narsGeoman.setCurrentPhase(pointPhase)
        val fresh = createRoad("Fresh").copy(id = "fresh-1")
        val staleFeatureData = mockk<FeatureData>(relaxed = true)
        every { geoman.features.getFeature(GeomanCoreConstants.SOURCE_MARKERS, "road-1") } returns staleFeatureData

        narsGeoman.addFeature(createRoad("Stale"))
        narsGeoman.updateDisplayedFeatures(listOf(fresh))

        verify { geoman.features.removeFeature(any(), "road-1") }
    }

    @Test
    fun `updateDisplayedFeatures adds road endpoint markers for roads phase`() {
        narsGeoman.setCurrentPhase(pointPhase)
        val features = listOf(createRoad("A"))
        narsGeoman.updateDisplayedFeatures(features)
        verify { labelAndMarkerManager.addRoadEndpointMarkers(features) }
    }

    // --- updateFeatureId ---

    @Test
    fun `updateFeatureId returns early when ids match`() {
        narsGeoman.updateFeatureId("same", "same")
        verify { featureRenderer wasNot Called }
    }

    @Test
    fun `updateFeatureId updates tracking when ids differ`() {
        narsGeoman.updateFeatureId("old", "new")
        verify { featureRenderer.removeFromTracking("old") }
    }

    // --- updateFeatureOnMap ---

    @Test
    fun `updateFeatureOnMap removes and adds feature when no GeoJsonSource`() {
        val feature = createRoad("R")
        narsGeoman.addFeature(feature)
        narsGeoman.updateFeatureOnMap(feature)
        verify { featureRenderer.addFeature(feature) }
    }

    // --- removeFeature ---

    @Test
    fun `removeFeature cleans up all systems`() {
        val featureData = mockk<FeatureData>(relaxed = true)
        every { geoman.features.getFeature(GeomanCoreConstants.SOURCE_MARKERS, "road-1") } returns featureData

        narsGeoman.removeFeature("road-1")

        verify { geoman.features.removeFeature(GeomanCoreConstants.SOURCE_MARKERS, "road-1") }
        verify { labelAndMarkerManager.removeVertexMarkers("road-1") }
        verify { featureRenderer.removeFromTracking("road-1") }
    }

    // --- clearAllFeatures ---

    @Test
    fun `clearAllFeatures clears rendering and tracking`() {
        narsGeoman.clearAllFeatures()
        verify { geoman.clearAllFeatures() }
        verify { featureRenderer.clearTracking() }
    }

    // --- snapPoint ---

    @Test
    fun `snapPoint delegates to snapping engine`() {
        val point = LatLng(36.0, 3.0)
        val features = listOf(createRoad())
        val snapped = LatLng(36.001, 3.001)
        every { snappingEngine.snapPoint(point, features, 20.0) } returns snapped

        val result = narsGeoman.snapPoint(point, features)
        assertEquals(snapped, result)
    }

    // --- onMapClick ---

    @Test
    fun `onMapClick delegates to draw click when drawing`() {
        narsGeoman.setCurrentPhase(pointPhase)
        narsGeoman.startDrawing()
        val latLng = LatLng(36.0, 3.0)

        every { geoman.getEnabledModes() } returns listOf(Pair(ModeType.DRAW, "line"))

        narsGeoman.onMapClick(latLng)

        verify { geoman.handleDrawClick("line", latLng) }
    }

    @Test
    fun `onMapClick does nothing when not drawing or editing`() {
        narsGeoman.onMapClick(LatLng(0.0, 0.0))
        verify { geoman wasNot Called }
    }

    // --- onMapLongClick ---

    @Test
    fun `onMapLongClick delegates to draw long press when drawing`() {
        narsGeoman.setCurrentPhase(pointPhase)
        narsGeoman.startDrawing()
        val latLng = LatLng(36.0, 3.0)

        every { geoman.getEnabledModes() } returns listOf(Pair(ModeType.DRAW, "line"))

        narsGeoman.onMapLongClick(latLng)

        verify { geoman.handleDrawLongPress("line", latLng) }
    }

    @Test
    fun `onMapLongClick does nothing when not drawing`() {
        narsGeoman.onMapLongClick(LatLng(0.0, 0.0))
        verify { geoman wasNot Called }
    }

    // --- destroy ---

    @Test
    fun `destroy is idempotent`() {
        narsGeoman.destroy()
        narsGeoman.destroy()
        verify(exactly = 1) { geoman.destroy() }
    }
}
