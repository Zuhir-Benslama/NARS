package com.nars.maplibre.modes

import com.geoman.maplibre.geoman.types.geojson.Feature as GeoJsonFeature
import com.geoman.maplibre.geoman.types.geojson.Geometry as GeoJsonGeometry
import com.nars.maplibre.data.model.CircleGeometry
import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.LineStringGeometry
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.Phases
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.PolygonGeometry
import io.mockk.every
import io.mockk.mockk
import io.mockk.verify
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.style.layers.FillLayer
import org.maplibre.android.style.layers.LineLayer
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource

class FeatureRendererTest {

    private lateinit var renderer: FeatureRenderer
    private lateinit var geoJsonSource: GeoJsonSource
    private lateinit var lineLayer: LineLayer
    private lateinit var fillLayer: FillLayer
    private lateinit var symbolLayer: SymbolLayer
    private lateinit var geometryConverter: GeometryConverter
    private lateinit var mockGeoJsonFeature: GeoJsonFeature
    private lateinit var mockGeoJsonGeometry: GeoJsonGeometry

    @Before
    fun setUp() {
        geoJsonSource = mockk(relaxed = true)
        lineLayer = mockk(relaxed = true)
        fillLayer = mockk(relaxed = true)
        symbolLayer = mockk(relaxed = true)
        geometryConverter = mockk(relaxed = true)
        mockGeoJsonFeature = mockk(relaxed = true)
        mockGeoJsonGeometry = mockk(relaxed = true)

        every { mockGeoJsonFeature.geometry } returns mockGeoJsonGeometry
        every { geometryConverter.convertToGeoJson(any()) } returns mockGeoJsonFeature
        every { geometryConverter.geometryToJson(any()) } returns """{"type":"Point","coordinates":[0,0]}"""

        renderer = FeatureRenderer(mockk(relaxed = true))
        renderer.labelAndMarkerManager = mockk(relaxed = true)
        renderer.geoJsonSourceFactory = { _, _ -> geoJsonSource }
        renderer.lineLayerFactory = { _, _ -> lineLayer }
        renderer.fillLayerFactory = { _, _ -> fillLayer }
        renderer.symbolLayerFactory = { _, _ -> symbolLayer }
        renderer.geometryConverterProvider = { geometryConverter }
    }

    private fun createRoad(geometry: com.nars.maplibre.data.model.Geometry = PointGeometry(coordinates = listOf(3.0, 36.0))): NarsFeature {
        return NarsFeature(
            id = "road-1",
            type = NarsFeatureType.ROAD,
            geometry = geometry,
            properties = FeatureProperties(phase = Phases.ROADS_KEY, color = "#3498db", name = "Main Road")
        )
    }

    @Test
    fun `isFeatureAdded returns true after adding`() {
        renderer.addFeature(createRoad())
        assertTrue(renderer.isFeatureAdded("road-1"))
    }

    @Test
    fun `isFeatureAdded returns false before adding`() {
        assertFalse(renderer.isFeatureAdded("road-1"))
    }

    @Test
    fun `addFeature skips duplicate ids`() {
        renderer.addFeature(createRoad())
        renderer.addFeature(createRoad())
        assertTrue(renderer.isFeatureAdded("road-1"))
    }

    @Test
    fun `removeFromTracking removes feature id`() {
        renderer.addFeature(createRoad())
        renderer.removeFromTracking("road-1")
        assertFalse(renderer.isFeatureAdded("road-1"))
    }

    @Test
    fun `clearTracking removes all feature ids`() {
        renderer.addFeature(createRoad())
        renderer.addFeature(
            createRoad().copy(
                id = "road-2",
                geometry = PointGeometry(coordinates = listOf(3.1, 36.1))
            )
        )
        renderer.clearTracking()
        assertEquals(0, renderer.getTrackedCount())
    }

    @Test
    fun `getTrackedCount returns correct count`() {
        assertEquals(0, renderer.getTrackedCount())
        renderer.addFeature(createRoad())
        assertEquals(1, renderer.getTrackedCount())
    }

    @Test
    fun `addFeature adds label for named point feature`() {
        renderer.addFeature(createRoad())
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "Main Road") }
    }

    @Test
    fun `addFeature passes null name for label`() {
        renderer.addFeature(createRoad().copy(properties = FeatureProperties(phase = Phases.ROADS_KEY, color = "#3498db")))
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), isNull()) }
    }

    @Test
    fun `addFeature passes blank name for label`() {
        renderer.addFeature(createRoad().copy(properties = FeatureProperties(phase = Phases.ROADS_KEY, color = "#3498db", name = "")))
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "") }
    }

    @Test
    fun `addFeature with LineStringGeometry adds label`() {
        renderer.addFeature(createRoad(LineStringGeometry(coordinates = listOf(3.0, 36.0, 3.1, 36.1))))
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "Main Road") }
    }

    @Test
    fun `addFeature with PolygonGeometry adds label`() {
        val poly = PolygonGeometry(coordinates = listOf(3.0, 36.0, 3.1, 36.0, 3.1, 36.1, 3.0, 36.1, 3.0, 36.0))
        renderer.addFeature(createRoad(poly))
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "Main Road") }
    }

    @Test
    fun `addFeature with CircleGeometry adds label`() {
        renderer.addFeature(createRoad(CircleGeometry(coordinates = listOf(3.0, 36.0, 50.0))))
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "Main Road") }
    }

    @Test
    fun `addFeature with PointGeometry creates symbol layer`() {
        renderer.addFeature(createRoad())
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "Main Road") }
    }

    @Test
    fun `addFeature with LineStringGeometry uses lineLayerFactory`() {
        renderer.addFeature(createRoad(LineStringGeometry(coordinates = listOf(3.0, 36.0, 3.1, 36.1))))
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "Main Road") }
    }

    @Test
    fun `addFeature with CircleGeometry uses fill and line layer factories`() {
        renderer.addFeature(createRoad(CircleGeometry(coordinates = listOf(3.0, 36.0, 50.0))))
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), "Main Road") }
    }

    @Test
    fun `addFeature uses geoJsonSourceFactory`() {
        renderer.addFeature(createRoad())
        verify { renderer.labelAndMarkerManager.addLabelLayer(any(), any(), any()) }
    }

    @Test
    fun `line width constants have expected values`() {
        assertEquals(2, FeatureRenderer.STYLE_LINE_WIDTH_THIN)
        assertEquals(3, FeatureRenderer.STYLE_LINE_WIDTH_MEDIUM)
        assertEquals(8, FeatureRenderer.STYLE_LINE_WIDTH_THICK)
    }
}
